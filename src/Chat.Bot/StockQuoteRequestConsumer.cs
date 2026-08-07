using Chat.Application.Contracts.Messaging;
using Chat.Application.Features.StockCommands.ResolveStockQuote;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.StockCommands;
using MassTransit;
using MediatR;

namespace Chat.Bot;

/// <summary>
/// The bot's inbound adapter: turns a <see cref="StockQuoteRequested"/> off
/// <see cref="MessagingConstants.StockQuoteRequestQueue"/> into one
/// <see cref="ResolveStockQuoteCommand"/> and nothing else.
/// </summary>
/// <remarks>
/// <b>There is no <c>BackgroundService</c> in this host, deliberately.</b> MassTransit's bus is already a
/// hosted service: it owns the connection, the channel, the prefetch window and the retry policy, and it
/// delivers messages to this consumer. A hand-rolled polling loop next to it would duplicate all of that
/// and lose the measured behaviour — four attempts two seconds apart, then the message moves to
/// <c>stock-quote-requests_error</c> instead of requeueing forever. The requirement a <c>BackgroundService</c>
/// would have been protecting — fully async, no blocking call, honours the stopping token — is met by
/// taking <c>ConsumeContext.CancellationToken</c> as the only token the use case ever sees.
/// <para>
/// <b>One rule for failures:</b> an expected failure is logged and acknowledged, because retrying an
/// identical message would reproduce it four times and then dead-letter something we already understand;
/// an unexpected exception propagates, so MassTransit applies its retry policy and finally the
/// <c>_error</c> queue. No manual nack path exists — that is the transport's job, not ours.
/// </para>
/// <para>
/// <b>Where the "the room always gets an answer" guarantee actually lives.</b>
/// <c>ResolveStockQuoteHandler</c> answers every command it is given; this consumer decides which
/// deliveries become a command, so the guarantee belongs to the pair: <i>every request Chat.Web publishes
/// is answered.</i> Two deliveries deliberately end without an answer, and neither can carry a waiting
/// participant today:
/// </para>
/// <list type="number">
/// <item>
/// A stock code this boundary rejects. Chat.Web can only publish <c>StockCode.Value</c>, and
/// <c>StockCode.Create</c> is idempotent over its own normalised output, so a rejection here means the
/// message was hand-crafted. Answering it would post a line into a room nobody asked from.
/// </item>
/// <item>
/// A command the pipeline rejects before the handler runs. Unreachable at present — no validator is
/// registered for <see cref="ResolveStockQuoteCommand"/>. <b>Adding one would make this a silent drop of a
/// legitimate request</b>, so it is logged at Error rather than Warning: a validator introduced without a
/// wording for its rejection is a gap to close here, not an expected outcome.
/// </item>
/// </list>
/// </remarks>
/// <param name="sender">Dispatches the use case through the same pipeline every other request uses.</param>
/// <param name="logger">Records the payloads this adapter refuses, with the correlation id to chase them by.</param>
internal sealed class StockQuoteRequestConsumer(ISender sender, ILogger<StockQuoteRequestConsumer> logger)
    : IConsumer<StockQuoteRequested>
{
    /// <summary>
    /// Maps the message onto the use case. Runs inside MassTransit's own scope, so the scoped publish
    /// endpoint the handler answers through carries this consume context and keeps the two hops correlated.
    /// </summary>
    /// <param name="context">The delivery, including the token that is cancelled when the bot stops.</param>
    public async Task Consume(ConsumeContext<StockQuoteRequested> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        StockQuoteRequested request = context.Message;

        // Re-validated at the boundary rather than trusted: the only legitimate publisher is Chat.Web,
        // which can only hold a validated code, so anything else on this queue was hand-crafted. Building
        // the value object here is also what keeps an unvalidated ticker out of the outbound Stooq URL.
        Result<StockCode> stockCode = StockCode.Create(request.StockCode);
        if (stockCode.IsFailure)
        {
            logger.LogWarning(
                "Discarding request {RequestId}: its stock code was rejected as {ErrorCode}. No retry can "
                + "make an invalid ticker valid, and no participant is waiting on a hand-crafted message.",
                request.RequestId,
                stockCode.Error.Code);

            return;
        }

        ResolveStockQuoteCommand command = new(
            request.RequestId,
            new ChatRoomId(request.ChatRoomId),
            stockCode.Value,
            request.RequestedByUserId);

        Result outcome = await sender.Send(command, context.CancellationToken).ConfigureAwait(false);

        if (outcome.IsFailure)
        {
            // The pipeline already logged the failure; this line names the message it belongs to, and says
            // out loud what the failure cost. Error, not Warning: the handler answers every command it
            // receives, so a rejection before it runs is the one way a request Chat.Web published ends
            // without a line in the room. See the remarks for why nothing reaches this today.
            logger.LogError(
                "Request {RequestId} for {StockCode} was rejected as {ErrorCode} before the use case ran, "
                + "so the room was left without an answer.",
                request.RequestId,
                stockCode.Value.Value,
                outcome.Error.Code);
        }
    }
}
