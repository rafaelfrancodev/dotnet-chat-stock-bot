using System.Diagnostics;
using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Messaging;
using Chat.Application.Abstractions.Stocks;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Messaging;
using Chat.Domain.Common;
using Chat.Domain.StockCommands;
using Microsoft.Extensions.Logging;

namespace Chat.Application.Features.StockCommands.ResolveStockQuote;

/// <summary>
/// The bot use case: one lookup, one sentence, one published answer.
/// </summary>
/// <remarks>
/// <b>It always answers every command it is given.</b> The participant is waiting on a chat line, so an
/// unknown ticker and an unreachable provider are outcomes with wording of their own rather than failures —
/// the room never gets silence. That is why <see cref="IStockQuoteProvider"/> is documented not to throw
/// for a Stooq problem, and why the only <c>catch</c> here is a backstop for a provider that breaks that
/// promise anyway. Which deliveries become a command at all is the consumer's decision, so the end-to-end
/// guarantee — every request Chat.Web publishes is answered — is stated on <c>Chat.Bot</c>'s
/// <c>StockQuoteRequestConsumer</c>, where both halves are visible.
/// <para>
/// <b>It is marked <see cref="IBotFeature"/></b>, so only <c>Chat.Bot</c> registers it, and its
/// constructor takes no persistence port at all: the bot never calls <c>AddPersistence()</c>, and a
/// handler that needed the database would fail the bot's startup instead of quietly gaining access to it.
/// </para>
/// <para>
/// <b>Publishing is deliberately outside the backstop.</b> A failed publish is not an answer the bot can
/// reword — it means the broker refused the message — so it propagates, and MassTransit retries the
/// consume and then moves it to <c>stock-quote-requests_error</c>. Swallowing it would lose the request
/// silently.
/// </para>
/// </remarks>
/// <param name="stockQuotes">The quote provider port; the Stooq adapter lives in Chat.Infrastructure.</param>
/// <param name="responder">Publishes the answer back through the broker, which is what "decoupled bot" means.</param>
/// <param name="clock">Stamps the answer; the handler never reads the ambient clock itself.</param>
/// <param name="logger">Records the resolved outcome — one line per <c>/stock=</c> command, not per message.</param>
internal sealed class ResolveStockQuoteHandler(
    IStockQuoteProvider stockQuotes,
    IStockQuoteResponder responder,
    IDateTimeProvider clock,
    ILogger<ResolveStockQuoteHandler> logger)
    : ICommandHandler<ResolveStockQuoteCommand>, IBotFeature
{
    public async Task<Result> Handle(ResolveStockQuoteCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        StockQuoteLookup lookup = await LookUpAsync(request.StockCode, cancellationToken).ConfigureAwait(false);

        StockQuoteResolved answer = new(
            RequestId: request.RequestId,
            ChatRoomId: request.ChatRoomId.Value,
            StockCode: request.StockCode.Value,
            RequestedByUserId: request.RequestedByUserId,
            Outcome: lookup.Outcome,
            Price: lookup.Price,
            Message: Describe(request.StockCode, lookup),
            ResolvedAtUtc: clock.UtcNow);

        await responder.RespondAsync(answer, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Resolved {StockCode} for request {RequestId} as {Outcome}.",
            request.StockCode.Value,
            request.RequestId,
            lookup.Outcome);

        return Result.Success();
    }

    /// <summary>
    /// Runs the lookup and guarantees a usable outcome comes back.
    /// </summary>
    /// <remarks>
    /// The provider already converts every Stooq failure mode into
    /// <see cref="StockQuoteLookup.LookupFailed"/>, so this <c>catch</c> is not the error handling for a
    /// third-party outage — it is the backstop for a provider that violates that contract (a bug, a
    /// misconfigured client, an adapter swapped in later). Logged at Error because it means our own code
    /// is wrong, unlike the provider's own Warning for "Stooq is having a bad day".
    /// <para>
    /// The caller's own cancellation is excluded and propagates: the bot is shutting down or the message
    /// was abandoned, and publishing "I could not reach the quote service" into a room nobody is waiting
    /// on would be noise. The filter tests the token rather than the exception type, because an
    /// <c>HttpClient</c> timeout also surfaces as <see cref="TaskCanceledException"/>.
    /// </para>
    /// </remarks>
    private async Task<StockQuoteLookup> LookUpAsync(StockCode stockCode, CancellationToken cancellationToken)
    {
        try
        {
            StockQuoteLookup lookup = await stockQuotes
                .GetQuoteAsync(stockCode, cancellationToken)
                .ConfigureAwait(false);

            return Verified(stockCode, lookup);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            logger.LogError(
                exception,
                "The quote provider threw while looking up {StockCode}, which its contract forbids. "
                + "Answering the room with a failed lookup.",
                stockCode.Value);

            return StockQuoteLookup.LookupFailed;
        }
    }

    /// <summary>
    /// Keeps the published outcome and the published price agreeing with each other.
    /// </summary>
    /// <remarks>
    /// <see cref="StockQuoteLookup.Quoted(decimal)"/> cannot produce a priceless quote, but the record's
    /// primary constructor can, and Chat.Web reads the outcome to decide whether to raise an outage alert.
    /// A "quoted" answer with no number is downgraded rather than rendered as <c>"$0.00 per share"</c> —
    /// noise dressed up as data, which is the same call the CSV parser makes about a zero close.
    /// </remarks>
    private StockQuoteLookup Verified(StockCode stockCode, StockQuoteLookup lookup)
    {
        if (lookup is not { Outcome: StockQuoteOutcome.Quoted, Price: null })
        {
            return lookup;
        }

        logger.LogError(
            "The quote provider reported {Outcome} for {StockCode} with no price. Answering the room with "
            + "a failed lookup instead of a quote of nothing.",
            StockQuoteOutcome.Quoted,
            stockCode.Value);

        return StockQuoteLookup.LookupFailed;
    }

    /// <summary>Picks the sentence that matches the outcome.</summary>
    /// <remarks>
    /// The <c>switch</c> ends in <see cref="UnreachableException"/> so a member added to
    /// <see cref="StockQuoteOutcome"/> fails loudly here instead of silently borrowing another outcome's
    /// wording. That is a programmer error visible at build time, not a runtime input the bot must survive.
    /// </remarks>
    private static string Describe(StockCode stockCode, StockQuoteLookup lookup) => lookup.Outcome switch
    {
        // Not null: Verified downgrades a priceless "quoted" outcome before this runs.
        StockQuoteOutcome.Quoted => StockQuoteAnswer.Quoted(stockCode, lookup.Price!.Value),
        StockQuoteOutcome.SymbolNotFound => StockQuoteAnswer.SymbolNotFound(stockCode),
        StockQuoteOutcome.LookupFailed => StockQuoteAnswer.Unavailable(stockCode),
        _ => throw new UnreachableException(
            $"Unhandled {nameof(StockQuoteOutcome)} '{lookup.Outcome}'. Every outcome needs wording of "
            + "its own before the bot can answer with it."),
    };

    /// <summary>
    /// Separates the caller giving up from the provider misbehaving. Only a signalled token means the
    /// cancellation was actually requested by the caller; a client timeout leaves it unsignalled.
    /// </summary>
    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
