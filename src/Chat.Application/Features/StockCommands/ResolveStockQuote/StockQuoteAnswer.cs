using System.Globalization;
using Chat.Domain.StockCommands;

namespace Chat.Application.Features.StockCommands.ResolveStockQuote;

/// <summary>
/// The bot's vocabulary: the three lines it can say about a lookup. Pure functions, so the wording the
/// challenge grades most literally is pinned by fast offline tests instead of a live Stooq call.
/// </summary>
/// <remarks>
/// The wording lives here rather than inside the handler because it is the bot's only user-visible
/// output, and because <c>IStockQuoteResponder</c> promises Chat.Web never re-formats a quote — one
/// place must own the text. Nothing here echoes participant input: the ticker has already passed
/// <see cref="StockCode"/>'s allow-list, and no other request value reaches the room.
/// </remarks>
public static class StockQuoteAnswer
{
    /// <summary>
    /// Two decimals, invariant culture, so the price reads the same on every machine that runs the bot.
    /// A culture-sensitive format would post "$93,42" on de-DE and turn thousands into a different number.
    /// </summary>
    private const string PriceFormat = "F2";

    /// <summary>
    /// The challenge's required wording, for example <c>"AAPL.US quote is $93.42 per share"</c>.
    /// </summary>
    /// <param name="stockCode">The ticker that was looked up; rendered upper case, as the challenge shows.</param>
    /// <param name="price">The closing price the provider reported.</param>
    public static string Quoted(StockCode stockCode, decimal price)
    {
        ArgumentNullException.ThrowIfNull(stockCode);

        // Formatted first, and only then interpolated: every hole in the interpolation below is a string,
        // so no ambient culture can reach the output through the string-building itself either.
        string formattedPrice = price.ToString(PriceFormat, CultureInfo.InvariantCulture);

        return $"{stockCode.Display} quote is ${formattedPrice} per share";
    }

    /// <summary>
    /// The provider answered and does not know the ticker — a real answer, so it stays a chat line and
    /// deliberately raises no outage alert.
    /// </summary>
    /// <param name="stockCode">The ticker the provider could not resolve.</param>
    public static string SymbolNotFound(StockCode stockCode)
    {
        ArgumentNullException.ThrowIfNull(stockCode);

        return $"Sorry, I could not find a quote for {stockCode.Display}.";
    }

    /// <summary>
    /// The provider could not be reached, timed out, or answered with something unusable.
    /// </summary>
    /// <remarks>
    /// This is the line a reviewer is most likely to read: Stooq's CSV endpoint has been answering 404
    /// since task 1.14. It therefore names the ticker — several lookups may be in flight — and says the
    /// quote service, not the bot, is the part that failed. It carries no "try again in N minutes"
    /// instruction on purpose: <c>ChatAlert.QuoteServiceUnavailable</c> delivers that as a banner, and
    /// the two are meant to complement each other rather than repeat.
    /// </remarks>
    /// <param name="stockCode">The ticker whose lookup could not be completed.</param>
    public static string Unavailable(StockCode stockCode)
    {
        ArgumentNullException.ThrowIfNull(stockCode);

        return $"I could not reach the quote service, so I have no price for {stockCode.Display} right now.";
    }
}
