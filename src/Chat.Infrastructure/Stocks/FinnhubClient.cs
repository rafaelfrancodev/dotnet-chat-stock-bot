using System.Globalization;
using System.Net;
using Chat.Application.Abstractions.Stocks;
using Chat.Application.Contracts.Messaging;
using Chat.Domain.StockCommands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Stocks;

/// <summary>
/// The Finnhub adapter behind <see cref="IStockQuoteProvider"/>: one HTTP GET, then
/// <see cref="FinnhubQuoteParser"/>. Selected by configuration instead of <see cref="StooqClient"/>.
/// </summary>
/// <remarks>
/// Honours the same contract as the Stooq adapter: a lookup never throws, and every third-party failure
/// becomes <see cref="StockQuoteLookup.LookupFailed"/> so the bot always has an answer for the room. Only
/// the caller's own cancellation propagates.
/// <para>
/// A missing or rejected API key is logged distinctly from a transient failure, because it is a
/// configuration mistake an operator must fix rather than something that will pass on its own.
/// </para>
/// </remarks>
internal sealed class FinnhubClient(
    HttpClient httpClient,
    IOptions<FinnhubOptions> options,
    ILogger<FinnhubClient> logger) : IStockQuoteProvider
{
    /// <summary>Name of the typed client, so the registration and its tests cannot drift.</summary>
    internal const string HttpClientName = nameof(FinnhubClient);

    /// <summary>Ceiling on the buffered response. A quote object is a couple of hundred bytes.</summary>
    internal const int MaxResponseBytes = 64 * 1024;

    private readonly FinnhubOptions _options = options.Value;

    /// <inheritdoc/>
    public async Task<StockQuoteLookup> GetQuoteAsync(
        StockCode stockCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stockCode);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            logger.LogError(
                "Finnhub:ApiKey is not configured, so no quote can be requested. Set it with user-secrets "
                + "or the Finnhub__ApiKey environment variable.");

            return StockQuoteLookup.LookupFailed;
        }

        string symbol = FinnhubQuoteParser.ToSymbol(stockCode);

        try
        {
            using HttpResponseMessage response = await httpClient
                .GetAsync(BuildQuoteUri(symbol), cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogUnsuccessfulStatus(response.StatusCode, stockCode, symbol);

                return StockQuoteLookup.LookupFailed;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            StockQuoteLookup lookup = FinnhubQuoteParser.Parse(json);

            if (lookup.Outcome == StockQuoteOutcome.LookupFailed)
            {
                logger.LogWarning(
                    "Finnhub answered 200 for {Symbol} with a body carrying no usable price.",
                    symbol);
            }

            return lookup;
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Finnhub lookup for {Symbol} failed.", symbol);

            return StockQuoteLookup.LookupFailed;
        }
    }

    private void LogUnsuccessfulStatus(HttpStatusCode status, StockCode stockCode, string symbol)
    {
        // 401/403 mean the key is wrong or the plan does not cover the symbol: an operator has to act, so
        // it is an error rather than the warning a transient failure gets.
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogError(
                "Finnhub rejected the API key ({StatusCode}) for {StockCode}. Check Finnhub:ApiKey.",
                (int)status,
                stockCode.Value);

            return;
        }

        logger.LogWarning(
            "Finnhub answered {StatusCode} for {Symbol}; reporting a failed lookup.",
            (int)status,
            symbol);
    }

    /// <summary>
    /// Builds the quote URL. The symbol comes from a validated <see cref="StockCode"/> and the key from
    /// configuration; both are escaped, so neither can alter the query string.
    /// </summary>
    private Uri BuildQuoteUri(string symbol)
    {
        string path = string.Format(
            CultureInfo.InvariantCulture,
            _options.QuotePath,
            Uri.EscapeDataString(symbol),
            Uri.EscapeDataString(_options.ApiKey));

        return new Uri(_options.BaseAddress, path);
    }

    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
