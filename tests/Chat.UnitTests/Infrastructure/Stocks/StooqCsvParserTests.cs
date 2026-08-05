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
