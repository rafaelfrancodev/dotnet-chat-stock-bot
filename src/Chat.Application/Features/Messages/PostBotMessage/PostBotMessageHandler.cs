using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Messaging;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Realtime;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Messages;
using Chat.Application.Contracts.Messaging;
using Chat.Application.Contracts.Realtime;
using Chat.Application.Errors;
using Chat.Domain.Common;
using Chat.Domain.Messages;
using Microsoft.Extensions.Logging;

namespace Chat.Application.Features.Messages.PostBotMessage;

/// <summary>
/// Closing half of the stock round trip: the bot's answer becomes a post owned by the bot and reaches the
/// room it was asked in.
/// </summary>
/// <remarks>
/// It is deliberately the same shape as <c>PostMessageHandler</c>'s write path — existence check, domain
/// factory, <c>Add</c>, one commit, then the broadcast — because it is the same use case with a different
/// author. Two consequences of that shape matter here:
/// <list type="bullet">
/// <item>
/// <b>Bot answers are persisted</b> (<see cref="MessageOrigin.Bot"/>), which is the standing decision
/// recorded in <c>ARCHITECTURE.md</c> §5: the challenge calls the answer a post whose owner is the bot, and
/// a reviewer who refreshes after a <c>/stock=</c> must still see it. Only the <b>command</b> is never
/// written, and that is enforced in <c>PostMessageHandler</c>, which this class is never reached from.
/// </item>
/// <item>
/// <b>The broadcast happens strictly after the commit.</b> Announcing first would show connected browsers
/// an answer a failed save never stored.
/// </item>
/// </list>
/// <para>
/// <b>The outage alert is raised here rather than in the consumer.</b> The consumer is a transport adapter:
/// it maps a payload onto a request and owns no user-visible behaviour. This handler already holds
/// <see cref="IChatNotifier"/> and already knows the outcome, and the alert has to be ordered against the
/// post (the banner explains the line the participant just read), so splitting the two would put "what the
/// participant sees when a lookup fails" in two layers and two projects at once.
/// </para>
/// </remarks>
/// <param name="chatRooms">Existence check, so a room deleted mid-flight is an expected failure.</param>
/// <param name="messages">Write side of the message store.</param>
/// <param name="unitOfWork">Commits the answer. Called exactly once.</param>
/// <param name="notifier">Room-scoped broadcast, and the participant-scoped outage alert.</param>
/// <param name="clock">Supplies the post time; the domain never reads the clock itself.</param>
/// <param name="logger">Records a fan-out failure, which is the one thing the caller cannot retry.</param>
internal sealed class PostBotMessageHandler(
    IChatRoomRepository chatRooms,
    IMessageRepository messages,
    IUnitOfWork unitOfWork,
    IChatNotifier notifier,
    IDateTimeProvider clock,
    ILogger<PostBotMessageHandler> logger)
    : ICommandHandler<PostBotMessageCommand>, IWebFeature
{
    public async Task<Result> Handle(PostBotMessageCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Same first gate as the participant path: one existence query, so a room that disappeared while
        // the bot was talking to Stooq costs neither a write nor a broadcast.
        bool roomExists = await chatRooms.ExistsAsync(request.ChatRoomId, cancellationToken).ConfigureAwait(false);
        if (!roomExists)
        {
            return Result.Failure(ChatRoomErrors.NotFound);
        }

        // The bot's wording still goes through the domain rule every post obeys, rather than being trusted
        // because it came from our own process: the column is bounded, so an over-long answer must fail
        // here as a Result instead of at the database as a truncation.
        Result<MessageContent> content = MessageContent.Create(request.Text);
        if (content.IsFailure)
        {
            return Result.Failure(content.Error);
        }

        Result<Message> message = Message.PostByBot(request.ChatRoomId, content.Value, clock.UtcNow);
        if (message.IsFailure)
        {
            return Result.Failure(message.Error);
        }

        messages.Add(message.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await AnnounceAsync(request, message.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Everything after the commit: the room sees the answer, and the participant who asked sees a banner
    /// when the answer is "the provider is down".
    /// </summary>
    /// <remarks>
    /// <b>A failure here is answered with <see cref="PostBotMessageCommand.Errors.NotAnnounced"/> instead
    /// of an exception, on purpose.</b> This runs under an at-least-once delivery: the consumer lets an
    /// exception propagate so MassTransit retries, and acknowledges a failed <see cref="Result"/>. The post
    /// is already committed by the time this method starts, so a retry would insert the same answer twice —
    /// permanently — whereas the connections that missed the push read it back from the last-50 history on
    /// their next join. Genuine caller cancellation still propagates: the host is stopping, the delivery
    /// will be redelivered by the transport whatever we return, and pretending otherwise would only hide it.
    /// </remarks>
    private async Task<Result> AnnounceAsync(
        PostBotMessageCommand request,
        Message message,
        CancellationToken cancellationToken)
    {
        try
        {
            await notifier
                .BroadcastMessageAsync(request.ChatRoomId, ToDto(message), cancellationToken)
                .ConfigureAwait(false);

            await AlertOnOutageAsync(request, cancellationToken).ConfigureAwait(false);

            return Result.Success();
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            logger.LogError(
                exception,
                "The bot's answer for room {ChatRoomId} was saved but could not be pushed to the "
                + "connected participants. Not retrying: the post exists, and a redelivery would "
                + "duplicate it. Connected clients recover it from the room history.",
                request.ChatRoomId.Value);

            return Result.Failure(PostBotMessageCommand.Errors.NotAnnounced);
        }
    }

    /// <summary>
    /// Tells the one participant who asked that the quote service is unreachable.
    /// </summary>
    /// <remarks>
    /// Only <see cref="StockQuoteOutcome.LookupFailed"/> qualifies. An unknown ticker
    /// (<see cref="StockQuoteOutcome.SymbolNotFound"/>) is a real answer from a working service, so it is a
    /// chat line and nothing more — raising a "the service is down, retry" banner for it would be false.
    /// <para>
    /// A payload with no requester carries nobody to alert. The only legitimate publisher fills the id from
    /// the caller's claims, so an empty one means a hand-crafted message; the answer is still posted, and
    /// the missing recipient is logged rather than turned into a failure of the whole use case.
    /// </para>
    /// </remarks>
    private async Task AlertOnOutageAsync(PostBotMessageCommand request, CancellationToken cancellationToken)
    {
        if (request.Outcome != StockQuoteOutcome.LookupFailed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
        {
            logger.LogWarning(
                "A failed lookup carried no requester, so no outage alert could be aimed at anybody. "
                + "The answer was still posted to room {ChatRoomId}.",
                request.ChatRoomId.Value);

            return;
        }

        await notifier
            .NotifyAlertAsync(request.RequestedByUserId, ChatAlert.QuoteServiceUnavailable, cancellationToken)
            .ConfigureAwait(false);
    }

    private static MessageDto ToDto(Message message) => new(
        message.Id.Value,
        message.Author.DisplayName,
        message.Content.Value,
        message.PostedAtUtc,
        message.Origin);

    /// <summary>
    /// Separates the host stopping from the realtime layer failing. Only a signalled token means the
    /// cancellation was actually requested, which is the same distinction <c>StooqClient</c> draws.
    /// </summary>
    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
