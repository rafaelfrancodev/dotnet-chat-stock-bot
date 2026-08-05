using Chat.Application.Contracts.Messaging;
using Chat.Application.Features.Messages.PostBotMessage;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using MassTransit;
using MediatR;

namespace Chat.Web.Messaging;

/// <summary>
/// Chat.Web's inbound adapter: turns a <see cref="StockQuoteResolved"/> off
/// <see cref="MessagingConstants.StockQuoteResponseQueue"/> into one
/// <see cref="PostBotMessageCommand"/> and nothing else.
/// </summary>
/// <remarks>
/// It is the mirror image of <c>Chat.Bot</c>'s request consumer, and follows the same two rules.
/// <list type="bullet">
/// <item>
/// <b>No manual nack or dead-letter path.</b> A payload MassTransit cannot deserialise never reaches this
/// class — the transport moves it to <c>stock-quote-responses_error</c> rather than requeueing it — and a
/// message this class throws on is retried by the configured policy (measured in task 1.10: four attempts
/// about two seconds apart) and then moved to the same queue. Writing our own nack would duplicate that.
/// </item>
/// <item>
/// <b>An expected failure is logged and acknowledged.</b> A failed <see cref="Result"/> is deterministic:
/// an identical redelivery reproduces it four times and then dead-letters something already understood.
/// That includes the answer having been saved but not broadcast, where a retry would duplicate the post —
/// see <see cref="PostBotMessageCommand.Errors.NotAnnounced"/>.
/// </item>
/// </list>
/// <para>
/// <b>The bot's wording crosses unchanged.</b> <c>Message</c> is passed to the use case verbatim; the
/// <c>Outcome</c> is carried because the handler decides from it whether to raise an outage banner, and the
/// <c>Price</c> is not carried at all — nothing on this side renders it.
/// </para>
/// </remarks>
/// <param name="sender">Dispatches the use case through the same pipeline every other request uses.</param>
/// <param name="logger">Records the answers this adapter could not post, with the correlation id.</param>
internal sealed class StockQuoteResponseConsumer(ISender sender, ILogger<StockQuoteResponseConsumer> logger)
    : IConsumer<StockQuoteResolved>
{
    /// <summary>
    /// Maps the answer onto the use case. Runs inside MassTransit's own scope, so the handler resolves the
    /// scoped <c>DbContext</c> and notifier it needs exactly as a hub call would.
    /// </summary>
    /// <param name="context">The delivery, including the token that is cancelled when the host stops.</param>
    public async Task Consume(ConsumeContext<StockQuoteResolved> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        StockQuoteResolved response = context.Message;

        PostBotMessageCommand command = new(
            new ChatRoomId(response.ChatRoomId),
            response.Message,
            response.RequestedByUserId,
            response.Outcome);

        Result outcome = await sender.Send(command, context.CancellationToken).ConfigureAwait(false);

        if (outcome.IsFailure)
        {
            // The pipeline already logged the failure; this line names the message it belongs to.
            logger.LogWarning(
                "Answer to request {RequestId} for {StockCode} was not posted: {ErrorCode}.",
                response.RequestId,
                response.StockCode,
                outcome.Error.Code);
        }
    }
}
