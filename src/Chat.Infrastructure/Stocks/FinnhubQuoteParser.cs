using System.Text.Json;
using Chat.Application.Abstractions.Stocks;
using Chat.Domain.StockCommands;

namespace Chat.Infrastructure.Stocks;

/// <summary>
/// Turns a Finnhub quote response into a <see cref="StockQuoteLookup"/>, and maps a chat ticker onto the
/// symbol Finnhub expects. Pure and static: no I/O, so every shape is covered offline.
/// </summary>
/// <remarks>
/// The response to <c>api/v1/quote</c> is a flat object; <c>c</c> is the current price and <c>pc</c> the
/// previous close:
/// <code>
/// {"c":261.74,"d":-0.15,"dp":-0.0573,"h":263.31,"l":260.68,"o":261.07,"pc":261.89,"t":1582641000}
/// </code>
/// A symbol Finnhub does not know comes back as a well-formed object with every number zero, which is the
/// unknown-symbol signal — the equivalent of Stooq's <c>N/D</c>. It is not an error status.
/// </remarks>
internal static class FinnhubQuoteParser
{
    /// <summary>Current price.</summary>
    internal const string PriceProperty = "c";

    /// <summary>Previous close, used only to tell "unknown symbol" from "price really is zero".</summary>
    internal const string PreviousCloseProperty = "pc";

    /// <summary>Market suffix the chat uses for US tickers, which Finnhub does not want.</summary>
    internal const string UnitedStatesSuffix = ".us";

    /// <summary>
    /// Maps a chat ticker onto a Finnhub symbol: <c>aapl.us</c> becomes <c>AAPL</c>, because Finnhub names
    /// US listings without a suffix. Other markets keep theirs (<c>shop.to</c> stays <c>SHOP.TO</c>), which
    /// is the form Finnhub uses for them.
    /// </summary>
    /// <param name="stockCode">The validated ticker as typed in the chat.</param>
    public static string ToSymbol(StockCode stockCode)
    {
        ArgumentNullException.ThrowIfNull(stockCode);

        string value = stockCode.Value;

        return value.EndsWith(UnitedStatesSuffix, StringComparison.Ordinal)
            ? value[..^UnitedStatesSuffix.Length].ToUpperInvariant()
            : stockCode.Display;
    }

    /// <summary>
    /// Reads the current price out of a Finnhub quote body.
    /// </summary>
    /// <param name="json">The response body, exactly as received.</param>
    /// <returns>
    /// <see cref="StockQuoteLookup.Quoted"/> for a usable price, <see cref="StockQuoteLookup.SymbolNotFound"/>
    /// when every number is zero, and <see cref="StockQuoteLookup.LookupFailed"/> for anything unreadable.
    /// Never throws.
    /// </returns>
    public static StockQuoteLookup Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return StockQuoteLookup.LookupFailed;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !TryReadNumber(document.RootElement, PriceProperty, out decimal price))
            {
                return StockQuoteLookup.LookupFailed;
            }

            if (price > 0)
            {
                return StockQuoteLookup.Quoted(price);
            }

            // Zero price with a zero previous close is how Finnhub answers a symbol it does not carry.
            // A zero price with a real previous close would be a genuine value, so it is not treated as
            // "unknown" — it simply is not a price the bot can quote.
            return TryReadNumber(document.RootElement, PreviousCloseProperty, out decimal previousClose)
                && previousClose == 0
                    ? StockQuoteLookup.SymbolNotFound
                    : StockQuoteLookup.LookupFailed;
        }
        catch (JsonException)
        {
            // An HTML error page or a truncated body: unreadable, so the service is the problem.
            return StockQuoteLookup.LookupFailed;
        }
    }

    private static bool TryReadNumber(JsonElement root, string property, out decimal value)
    {
        value = 0;

        return root.TryGetProperty(property, out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDecimal(out value);
    }
}
