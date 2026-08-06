using System.Globalization;
using Chat.Application.Abstractions.Stocks;

namespace Chat.Infrastructure.Stocks;

/// <summary>
/// Turns a Stooq CSV response into a <see cref="StockQuoteLookup"/>. Pure and static: it does no I/O, so
/// every shape Stooq can answer with — a quote, an unknown ticker, a truncated body, an HTML error page —
/// is covered by fast tests instead of by a live call.
/// </summary>
/// <remarks>
/// Two response shapes are supported, because Stooq serves the quote under two different paths.
/// <para>
/// <c>/q/l/?f=sd2t2ohlcv&amp;h&amp;e=csv</c> — a header and a single data line:
/// <code>
/// Symbol,Date,Time,Open,High,Low,Close,Volume
/// AAPL.US,2026-08-04,21:00:00,205.1,207.2,204.4,206.55,42193021
/// </code>
/// </para>
/// <para>
/// <c>/q/d/l/?s=aa.us</c> — the daily history, a header and one line per session, oldest first:
/// <code>
/// Date,Open,High,Low,Close,Volume
/// 2026-08-04,46.83,47.12,46.40,46.83,3912004
/// 2026-08-05,46.90,47.85,46.88,47.69,4910000
/// </code>
/// </para>
/// The <b>last</b> data line is the one read, which is the only line in the first shape and the newest
/// session in the second. The price comes from locating the <see cref="PriceColumn"/> header, never from a
/// fixed position: the column order follows the request, and a positional read would silently quote the
/// wrong number the moment the path or the <c>f=</c> parameter changed.
/// </remarks>
internal static class StooqCsvParser
{
    /// <summary>Header of the column the bot quotes — the last closing price.</summary>
    internal const string PriceColumn = "Close";

    /// <summary>What Stooq writes in the data fields of a ticker it does not know.</summary>
    internal const string NotAvailable = "N/D";

    private const char FieldSeparator = ',';
    private const int HeaderAndDataLines = 2;

    private static readonly char[] LineSeparators = ['\r', '\n'];

    /// <summary>
    /// Reads the closing price out of a Stooq CSV body.
    /// </summary>
    /// <param name="csv">The response body, exactly as received.</param>
    /// <returns>
    /// <see cref="StockQuoteLookup.Quoted"/> with the closing price for a usable row,
    /// <see cref="StockQuoteLookup.SymbolNotFound"/> when Stooq answered <see cref="NotAvailable"/>, and
    /// <see cref="StockQuoteLookup.LookupFailed"/> for anything it cannot make sense of. Never throws.
    /// </returns>
    public static StockQuoteLookup Parse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return StockQuoteLookup.LookupFailed;
        }

        string[] lines = csv.Split(
            LineSeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length < HeaderAndDataLines)
        {
            return StockQuoteLookup.LookupFailed;
        }

        string[] header = lines[0].Split(FieldSeparator);

        // The newest session, and the only row when the single-quote path answered. Deliberately not a
        // scan backwards for the first parsable row: if the latest line is unusable, the honest answer is
        // "no price" rather than yesterday's close presented as today's quote.
        string[] row = lines[^1].Split(FieldSeparator);
        int priceIndex = Array.FindIndex(header, IsPriceColumn);

        // A row that does not line up with its own header is not a quote we can read, whatever it is.
        if (priceIndex < 0 || row.Length != header.Length)
        {
            return StockQuoteLookup.LookupFailed;
        }

        string price = row[priceIndex].Trim();

        if (price.Equals(NotAvailable, StringComparison.OrdinalIgnoreCase))
        {
            return StockQuoteLookup.SymbolNotFound;
        }

        // InvariantCulture explicitly: InvariantGlobalization is off in this solution, so a de-DE host
        // would read "206.55" as a grouped 20655 — or reject it outright — with the ambient culture.
        bool parsed = decimal.TryParse(
            price,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out decimal closingPrice);

        return parsed && closingPrice > 0
            ? StockQuoteLookup.Quoted(closingPrice)
            : StockQuoteLookup.LookupFailed;
    }

    private static bool IsPriceColumn(string column) =>
        column.Trim().Equals(PriceColumn, StringComparison.OrdinalIgnoreCase);
}
