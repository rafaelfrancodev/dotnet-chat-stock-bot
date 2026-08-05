using System.Globalization;
using Chat.Application.Abstractions.Stocks;
using Chat.Application.Contracts.Messaging;
using Chat.Domain.StockCommands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Stocks;

/// <summary>
/// The Stooq adapter behind <see cref="IStockQuoteProvider"/>: one HTTP GET, then
/// <see cref="StooqCsvParser"/>. Registered as a typed client by
/// <see cref="DependencyInjection.AddStockQuotes"/>, so the handler and the connection pool are owned by
/// <c>IHttpClientFactory</c> rather than created per lookup.
/// </summary>
/// <remarks>
/// Honours the port's contract that a lookup never throws: an unreachable service, a timeout, a 5xx, an
/// HTML error page and a truncated body all become <see cref="StockQuoteLookup.LookupFailed"/>, because
/// the bot owes the chat room an answer either way. The one exception that still propagates is the
/// caller's own cancellation — the bot is shutting down or the message was abandoned, and inventing a
/// "lookup failed" answer for it would post noise into a room nobody is waiting on.
/// </remarks>
internal sealed class StooqClient(
    HttpClient httpClient,
    IOptions<StooqOptions> options,
    ILogger<StooqClient> logger) : IStockQuoteProvider
{
    /// <summary>Name of the typed client, so the registration and its tests cannot drift.</summary>
    internal const string HttpClientName = nameof(StooqClient);

    /// <summary>
    /// Attempts spent on one lookup, initial call included. Also divides the request budget into the
    /// per-attempt timeout, so a slow Stooq cannot consume the whole budget on its first try.
    /// </summary>
    internal const int MaxAttemptsPerLookup = 3;

    /// <summary>Base backoff between attempts; the standard handler grows it exponentially with jitter.</summary>
    internal const int RetryDelayMilliseconds = 250;

    /// <summary>
    /// Ceiling on the buffered response. A quote row is ~70 bytes; this only stops a redirected or
    /// hostile endpoint from streaming an unbounded body into the bot's memory.
    /// </summary>
    internal const int MaxResponseBytes = 64 * 1024;

    private readonly StooqOptions _options = options.Value;

    /// <inheritdoc/>
    public async Task<StockQuoteLookup> GetQuoteAsync(
        StockCode stockCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stockCode);

        Uri requestUri = BuildQuoteUri(stockCode);

        try
        {
            using HttpResponseMessage response = await httpClient
                .GetAsync(requestUri, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Stooq answered {StatusCode} for {StockCode}; reporting a failed lookup.",
                    (int)response.StatusCode,
                    stockCode.Value);

                return StockQuoteLookup.LookupFailed;
            }

            string csv = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            StockQuoteLookup lookup = StooqCsvParser.Parse(csv);

            if (lookup.Outcome == StockQuoteOutcome.LookupFailed)
            {
                logger.LogWarning(
                    "Stooq returned a body for {StockCode} that carries no usable {PriceColumn} price.",
                    stockCode.Value,
                    StooqCsvParser.PriceColumn);
            }

            return lookup;
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            // Deliberately broad. This is the boundary where a third party's failure mode — transport
            // error, timeout, open circuit, oversized body — is converted into the outcome the bot can
            // speak. Warning, not error: it is one line per failed /stock= command, not per message.
            logger.LogWarning(exception, "Stooq lookup for {StockCode} failed.", stockCode.Value);

            return StockQuoteLookup.LookupFailed;
        }
    }

    /// <summary>
    /// Builds the quote URL from the configured base address and path template.
    /// </summary>
    /// <remarks>
    /// The only variable part is a <see cref="StockCode"/>, which has already passed the
    /// <c>^[a-z0-9.\-]{1,20}$</c> allow-list, so no raw input can reach the query string.
    /// <see cref="Uri.EscapeDataString(string)"/> is applied anyway: it is a no-op for every character
    /// the allow-list permits, and it keeps this line correct if the allow-list is ever widened.
    /// </remarks>
    private Uri BuildQuoteUri(StockCode stockCode)
    {
        string path = string.Format(
            CultureInfo.InvariantCulture,
            _options.QuotePath,
            Uri.EscapeDataString(stockCode.Value));

        return new Uri(_options.BaseAddress, path);
    }

    /// <summary>
    /// Separates the caller giving up from Stooq being slow. An <c>HttpClient</c> timeout also surfaces
    /// as a <see cref="TaskCanceledException"/>, but with the caller's token still unsignalled — only a
    /// signalled token means the cancellation was actually requested by the caller.
    /// </summary>
    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
