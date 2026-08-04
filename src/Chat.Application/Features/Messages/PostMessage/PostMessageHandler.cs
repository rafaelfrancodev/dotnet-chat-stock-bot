using System.Diagnostics;
using Chat.Application.Abstractions.Messaging;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Realtime;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Messages;
using Chat.Application.Errors;
using Chat.Application.Features.StockCommands.RequestStockQuote;
using Chat.Domain.Common;
using Chat.Domain.Messages;
using Chat.Domain.StockCommands;
using MediatR;

namespace Chat.Application.Features.Messages.PostMessage;

/// <summary>
/// The branch point of the whole challenge: it decides whether a line of chat input becomes a row in the
/// messages table or a request handed to the bot.
/// </summary>
/// <remarks>
/// <b>The hard rule — a <c>/stock=</c> command is never saved as a post — is enforced by structure, not
/// by discipline:</b>
/// <list type="number">
/// <item>
/// The input is classified by <see cref="ChatCommandParser"/> before anything else happens, and the
/// classification is a closed type hierarchy, so the branch is a type <c>switch</c> rather than a string
/// test somebody can get subtly wrong.
/// </item>
/// <item>
/// <see cref="PostAsync"/> is the only method that touches <see cref="IMessageRepository"/> and
/// <see cref="IUnitOfWork"/>, and it takes a <see cref="ParsedChatInput.PlainMessage"/>. Persisting a
/// stock command would require turning a <see cref="ParsedChatInput.StockQuote"/> into a plain message,
/// which the type system does not allow.
/// </item>
/// <item>
/// The stock branch does not publish either: it dispatches <see cref="RequestStockQuoteCommand"/>, whose
/// handler has no persistence dependency at all, so no code reachable from that branch can write a row.
/// </item>
/// <item>
/// The <c>switch</c> ends in <see cref="UnreachableException"/>. A fifth case added to
/// <see cref="ParsedChatInput"/> fails loudly instead of falling through to the persist path.
/// </item>
/// </list>
/// </remarks>
/// <param name="chatRooms">Existence check, so an unknown room is an expected failure, not a write error.</param>
/// <param name="messages">Write side of the message store. Reached only from the plain-message branch.</param>
/// <param name="unitOfWork">Commits the post. Called exactly once, and only on the plain-message branch.</param>
/// <param name="notifier">Room-scoped broadcast, performed only after a successful commit.</param>
/// <param name="clock">Supplies the post time; the domain never reads the clock itself.</param>
/// <param name="sender">Dispatches the stock command as its own use case.</param>
internal sealed class PostMessageHandler(
    IChatRoomRepository chatRooms,
    IMessageRepository messages,
    IUnitOfWork unitOfWork,
    IChatNotifier notifier,
    IDateTimeProvider clock,
    ISender sender)
    : ICommandHandler<PostMessageCommand, PostMessageOutcome>
{
    public async Task<Result<PostMessageOutcome>> Handle(
        PostMessageCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // First and cheapest gate: one existence query. An unknown room therefore never persists, never
        // publishes and never broadcasts, whatever the participant typed.
        bool roomExists = await chatRooms.ExistsAsync(request.ChatRoomId, cancellationToken).ConfigureAwait(false);
        if (!roomExists)
        {
            return Result.Failure<PostMessageOutcome>(ChatRoomErrors.NotFound);
        }

        // Classify before doing anything with the text: what this line *is* decides which branch may run.
        ParsedChatInput parsed = ChatCommandParser.Parse(request.RawInput);

        return parsed switch
        {
            ParsedChatInput.PlainMessage plainMessage =>
                await PostAsync(request, plainMessage, cancellationToken).ConfigureAwait(false),

            ParsedChatInput.StockQuote stockQuote =>
                await RequestQuoteAsync(request, stockQuote, cancellationToken).ConfigureAwait(false),

            // Untrusted input: the command name is never echoed back into the error message.
            ParsedChatInput.UnknownCommand =>
                Result.Failure<PostMessageOutcome>(PostMessageCommand.Errors.UnknownCommand),

            // The parser owns the ticker rules, so its error is returned unchanged rather than restated.
            ParsedChatInput.Invalid invalid => Result.Failure<PostMessageOutcome>(invalid.Error),

            _ => throw new UnreachableException(
                $"Unhandled {nameof(ParsedChatInput)} case '{parsed.GetType().Name}'. "
                + "A new case must be classified explicitly before it can reach the message store."),
        };
    }

    /// <summary>
    /// The one and only write path. It accepts a <see cref="ParsedChatInput.PlainMessage"/>, which is what
    /// makes "a stock command cannot be persisted" a compile-time property of this class.
    /// </summary>
    private async Task<Result<PostMessageOutcome>> PostAsync(
        PostMessageCommand request,
        ParsedChatInput.PlainMessage plainMessage,
        CancellationToken cancellationToken)
    {
        Result<MessageAuthor> author = MessageAuthor.Create(request.AuthorUserId, request.AuthorDisplayName);
        if (author.IsFailure)
        {
            return Result.Failure<PostMessageOutcome>(author.Error);
        }

        Result<MessageContent> content = MessageContent.Create(plainMessage.Text);
        if (content.IsFailure)
        {
            return Result.Failure<PostMessageOutcome>(content.Error);
        }

        Result<Message> message = Message.PostByParticipant(
            request.ChatRoomId,
            author.Value,
            content.Value,
            clock.UtcNow);

        if (message.IsFailure)
        {
            return Result.Failure<PostMessageOutcome>(message.Error);
        }

        messages.Add(message.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Broadcast strictly after the commit. Announcing first would show connected browsers a message
        // that a failed save never stored — a phantom post that disappears on the next reload.
        await notifier
            .BroadcastMessageAsync(request.ChatRoomId, ToDto(message.Value), cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(PostMessageOutcome.Posted);
    }

    /// <summary>
    /// The stock branch. It touches no repository, no unit of work and no notifier: the command is not a
    /// post, and the answer arrives later, asynchronously, from the bot.
    /// </summary>
    private async Task<Result<PostMessageOutcome>> RequestQuoteAsync(
        PostMessageCommand request,
        ParsedChatInput.StockQuote stockQuote,
        CancellationToken cancellationToken)
    {
        RequestStockQuoteCommand quoteRequest = new(
            request.ChatRoomId,
            stockQuote.Code,
            request.AuthorUserId,
            request.AuthorDisplayName);

        Result dispatch = await sender.Send(quoteRequest, cancellationToken).ConfigureAwait(false);

        return dispatch.IsSuccess
            ? Result.Success(PostMessageOutcome.QuoteRequested)
            : Result.Failure<PostMessageOutcome>(dispatch.Error);
    }

    private static MessageDto ToDto(Message message) => new(
        message.Id.Value,
        message.Author.DisplayName,
        message.Content.Value,
        message.PostedAtUtc,
        message.Origin);
}
