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
/// and lose the parts that were measured in task 1.10 — four attempts two seconds apart, then the message
/// moves to <c>stock-quote-requests_error</c> instead of requeueing forever. The plan's original wording
/// ("the worker is a <c>BackgroundService</c>") predates the move from raw <c>RabbitMQ.Client</c> to
/// MassTransit in task 0.9; the requirement it was protecting — fully async, no blocking call, honours the
/// stopping token — is met by taking <c>ConsumeContext.CancellationToken</c> as the only token the use
/// case ever sees.
/// <para>
/// <b>One rule for failures:</b> an expected failure is logged and acknowledged, because retrying an
/// identical message would reproduce it four times and then dead-letter something we already understand;
/// an unexpected exception propagates, so MassTransit applies its retry policy and finally the
/// <c>_error</c> queue. No manual nack path exists — that is the transport's job, not ours.
/// </para>
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
            // The pipeline already logged the failure; this line names the message it belongs to.
            logger.LogWarning(
                "Request {RequestId} for {StockCode} was rejected as {ErrorCode}.",
                request.RequestId,
                stockCode.Value.Value,
                outcome.Error.Code);
        }
    }
}
