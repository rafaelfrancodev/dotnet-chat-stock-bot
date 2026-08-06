using System.Globalization;
using Chat.Application.Abstractions.Stocks;
using Chat.Application.Contracts.Messaging;
using Chat.Infrastructure.Stocks;

namespace Chat.UnitTests.Infrastructure.Stocks;

/// <summary>
/// Covers every shape Stooq can answer with. The parser does no I/O, so all of this runs offline —
/// the live endpoint is deliberately never touched by a test.
/// </summary>
public sealed class StooqCsvParserTests
{
    private const string Header = "Symbol,Date,Time,Open,High,Low,Close,Volume";
    private const string QuoteRow = "AAPL.US,2026-08-04,21:00:00,205.1,207.2,204.4,206.55,42193021";
    private const string NotFoundRow = "ZZZZ.US,N/D,N/D,N/D,N/D,N/D,N/D,N/D";

    /// <summary>Header of the daily-history download served by <c>/q/d/l/</c>.</summary>
    private const string HistoryHeader = "Date,Open,High,Low,Close,Volume";

    [Fact]
    public void Parse_ValidRow_ReturnsClosePrice()
    {
        StockQuoteLookup lookup = StooqCsvParser.Parse($"{Header}\r\n{QuoteRow}\r\n");

        lookup.Outcome.Should().Be(StockQuoteOutcome.Quoted);
        lookup.Price.Should().Be(206.55m);
    }

    /// <summary>
    /// The <c>Close</c> column is the fourth of five OHLC fields, so a positional read would quote the
    /// low instead. This pins that the header name is what locates the price.
    /// </summary>
    [Fact]
    public void Parse_ValidRow_ReadsThePriceFromTheCloseColumnAndNotTheNeighbouringOnes()
    {
        StockQuoteLookup lookup = StooqCsvParser.Parse($"{Header}\n{QuoteRow}");

        lookup.Price.Should().Be(206.55m, "the Close column is 206.55; Open/High/Low/Volume are not");
    }

    /// <summary>
    /// The daily-history download (<c>/q/d/l/?s=aa.us</c>) returns one line per session, oldest first, so
    /// the quote is the <b>last</b> row — not the first, which would report a price from months ago.
    /// </summary>
    [Fact]
    public void Parse_DailyHistory_ReturnsTheCloseOfTheNewestSession()
    {
        string csv = string.Join(
            '\n',
            HistoryHeader,
            "2026-08-03,46.10,46.55,45.90,46.20,3110022",
            "2026-08-04,46.83,47.12,46.40,46.83,3912004",
            "2026-08-05,46.90,47.85,46.88,47.69,4910000");

        StockQuoteLookup lookup = StooqCsvParser.Parse(csv);

        lookup.Outcome.Should().Be(StockQuoteOutcome.Quoted);
        lookup.Price.Should().Be(47.69m, "the newest session closed at 47.69, not the first row's 46.20");
    }

    /// <summary>
    /// The real body captured from <c>/q/d/l/?s=aavvf.us&amp;i=d</c>, trimmed to its first and last
    /// sessions. The quote is the last row's close, 7.69 — the first row's 7.84 is five months old.
    /// </summary>
    [Fact]
    public void Parse_ARealDailyHistoryBody_QuotesTheLastSessionsClose()
    {
        string csv = string.Join(
            '\n',
            "Date,Open,High,Low,Close,Volume",
            "2026-03-09,8,8.09,7.77,7.84,134600",
            "2026-03-10,7.9,7.9,7.67,7.78,89100",
            "2026-08-04,7.78,7.8,7.68,7.68,136000",
            "2026-08-05,7.51,7.71,7.5,7.69,1250");

        StockQuoteLookup lookup = StooqCsvParser.Parse(csv);

        lookup.Outcome.Should().Be(StockQuoteOutcome.Quoted);
        lookup.Price.Should().Be(7.69m);
    }

    /// <summary>
    /// Stooq answers a symbol it will not serve with <c>Access denied</c> and HTTP <b>200</b>, so the
    /// status code cannot tell a bad ticker from a broken service — the body has to. A participant who
    /// mistyped a ticker must be told the symbol was not found, not that the service is down.
    /// </summary>
    [Theory]
    [InlineData("Access denied")]
    [InlineData("Access denied\n")]
    [InlineData("access denied")]
    [InlineData("  Access denied  ")]
    public void Parse_AccessDeniedBody_ReturnsSymbolNotFound(string body)
    {
        StooqCsvParser.Parse(body).Outcome.Should().Be(StockQuoteOutcome.SymbolNotFound);
    }

    /// <summary>
    /// The browser-verification page, by contrast, says nothing about the ticker: it means no client
    /// without a solved challenge can read anything, which is the service being unusable.
    /// </summary>
    [Fact]
    public void Parse_BrowserVerificationPage_ReturnsLookupFailed()
    {
        const string challenge =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body><noscript>This site requires "
            + "JavaScript to verify your browser.</noscript><script>/* proof of work */</script></body></html>";

        StooqCsvParser.Parse(challenge).Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    /// <summary>A history with a single session behaves like the single-quote response.</summary>
    [Fact]
    public void Parse_DailyHistoryWithOneSession_ReturnsThatClose()
    {
        StockQuoteLookup lookup = StooqCsvParser.Parse($"{HistoryHeader}\r\n2026-08-05,46.90,47.85,46.88,47.69,4910000\r\n");

        lookup.Price.Should().Be(47.69m);
    }

    /// <summary>
    /// A truncated newest row is a failed lookup, not an excuse to quote the previous session: reporting
    /// an older close as the current price would be worse than reporting no price at all.
    /// </summary>
    [Fact]
    public void Parse_DailyHistoryWithATruncatedNewestRow_ReturnsLookupFailed()
    {
        string csv = string.Join(
            '\n',
            HistoryHeader,
            "2026-08-04,46.83,47.12,46.40,46.83,3912004",
            "2026-08-05,46.90,47.85");

        StooqCsvParser.Parse(csv).Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    /// <summary>
    /// The column order follows the <c>f=</c> query parameter. Parsing by header name means editing that
    /// parameter cannot silently start quoting a different number.
    /// </summary>
    [Fact]
    public void Parse_ReorderedColumns_StillReadsTheClosePrice()
    {
        StockQuoteLookup lookup = StooqCsvParser.Parse("Symbol,Close,Volume\nAAPL.US,206.55,42193021");

        lookup.Outcome.Should().Be(StockQuoteOutcome.Quoted);
        lookup.Price.Should().Be(206.55m);
    }

    [Fact]
    public void Parse_NotAvailableRow_ReturnsSymbolNotFound()
    {
        StockQuoteLookup lookup = StooqCsvParser.Parse($"{Header}\r\n{NotFoundRow}\r\n");

        lookup.Outcome.Should().Be(StockQuoteOutcome.SymbolNotFound);
        lookup.Price.Should().BeNull();
    }

    [Fact]
    public void Parse_HeaderOnly_ReturnsLookupFailed()
    {
        StockQuoteLookup lookup = StooqCsvParser.Parse($"{Header}\r\n");

        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
        lookup.Price.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    [InlineData(null)]
    public void Parse_NoBody_ReturnsLookupFailed(string? csv)
    {
        StooqCsvParser.Parse(csv).Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    [Theory]
    // Fewer fields than the header declares.
    [InlineData("Symbol,Date,Time,Open,High,Low,Close,Volume\nAAPL.US,2026-08-04")]
    // More fields than the header declares.
    [InlineData("Symbol,Close\nAAPL.US,206.55,42193021")]
    // No Close column at all.
    [InlineData("Symbol,Date,Time,Open,High,Low,Volume\nAAPL.US,2026-08-04,21:00:00,205.1,207.2,204.4,42193021")]
    // A price that is not a number.
    [InlineData("Symbol,Close\nAAPL.US,not-a-price")]
    // An empty price field.
    [InlineData("Symbol,Close\nAAPL.US,")]
    // Not CSV at all: the HTML error page Stooq actually serves for an unknown path.
    [InlineData("<meta charset=utf-8><title>Stooq</title><p>The page you requested does not exist")]
    // Plain prose, and a single line with no data row.
    [InlineData("garbage")]
    [InlineData("{\"error\":\"nope\"}")]
    public void Parse_MalformedRow_ReturnsLookupFailed(string csv)
    {
        StockQuoteLookup lookup = StooqCsvParser.Parse(csv);

        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
        lookup.Price.Should().BeNull();
    }

    /// <summary>
    /// A zero or negative close is not a quote the room can act on; "$0.00 per share" would be noise
    /// dressed up as data.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-12.5")]
    public void Parse_NonPositivePrice_ReturnsLookupFailed(string price)
    {
        StooqCsvParser.Parse($"Symbol,Close\nAAPL.US,{price}")
            .Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    /// <summary>
    /// <c>InvariantGlobalization</c> is deliberately <c>false</c> in this solution (SqlClient requires
    /// it), so the ambient culture is real: on a de-DE host a culture-sensitive parse rejects
    /// <c>206.55</c> outright. The guard assertion keeps this test from passing vacuously if the chosen
    /// culture ever stops disagreeing with the invariant one.
    /// </summary>
    [Fact]
    public void Parse_CommaDecimalCulture_StillParsesInvariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            decimal.TryParse("206.55", NumberStyles.Float, CultureInfo.CurrentCulture, out _)
                .Should().BeFalse("the culture under test must disagree with the invariant one");

            StockQuoteLookup lookup = StooqCsvParser.Parse($"{Header}\n{QuoteRow}");

            lookup.Outcome.Should().Be(StockQuoteOutcome.Quoted);
            lookup.Price.Should().Be(206.55m);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>Documents the response the challenge's <c>f=sd2t2ohlcv&amp;h</c> parameter produces.</summary>
    [Fact]
    public void PriceColumn_IsTheCloseColumnOfTheDocumentedHeader()
    {
        Header.Split(',').Should().Contain(StooqCsvParser.PriceColumn);
        StooqCsvParser.NotAvailable.Should().Be("N/D");
    }
}
