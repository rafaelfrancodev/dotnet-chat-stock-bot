using System.Globalization;
using Chat.Application.Abstractions.Stocks;
using Chat.Application.Contracts.Messaging;
using Chat.Domain.Common;
using Chat.Domain.StockCommands;
using Chat.Infrastructure.Stocks;

namespace Chat.UnitTests.Infrastructure.Stocks;

/// <summary>
/// Covers the Finnhub response shapes. The bodies here are the real ones, captured from the live API on
/// 2026-08-06, so the parser is pinned against what the service actually sends rather than a guess.
/// </summary>
public sealed class FinnhubQuoteParserTests
{
    /// <summary>A real quote for AAPL.</summary>
    private const string QuoteBody =
        """{"c":311.55,"d":0.55,"dp":0.1768,"h":316.2894,"l":309.23,"o":313.73,"pc":311,"t":1786038273}""";

    /// <summary>A symbol Finnhub does not carry: HTTP 200, every number zero.</summary>
    private const string UnknownSymbolBody =
        """{"c":0,"d":null,"dp":null,"h":0,"l":0,"o":0,"pc":0,"t":0}""";

    [Fact]
    public void Parse_RealQuote_ReturnsTheCurrentPrice()
    {
        StockQuoteLookup lookup = FinnhubQuoteParser.Parse(QuoteBody);

        lookup.Outcome.Should().Be(StockQuoteOutcome.Quoted);
        lookup.Price.Should().Be(311.55m, "c is the current price; o/h/l/pc are not");
    }

    /// <summary>
    /// All-zero numbers with HTTP 200 is Finnhub's unknown-symbol answer — the equivalent of Stooq's
    /// <c>N/D</c>. It must be a friendly "not found", never the outage banner, because the service is
    /// working perfectly well.
    /// </summary>
    [Fact]
    public void Parse_UnknownSymbol_ReturnsSymbolNotFound()
    {
        StockQuoteLookup lookup = FinnhubQuoteParser.Parse(UnknownSymbolBody);

        lookup.Outcome.Should().Be(StockQuoteOutcome.SymbolNotFound);
        lookup.Price.Should().BeNull();
    }

    /// <summary>
    /// A zero current price with a real previous close is not an unknown symbol — the symbol exists. It is
    /// simply not a price worth quoting, so it is a failed lookup rather than a false "not found".
    /// </summary>
    [Fact]
    public void Parse_ZeroPriceButRealPreviousClose_ReturnsLookupFailed()
    {
        StockQuoteLookup lookup = FinnhubQuoteParser.Parse("""{"c":0,"pc":311,"t":1786038273}""");

        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("<html><body>gateway error</body></html>")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"error":"Invalid API key"}""")]
    [InlineData("""{"c":"not-a-number"}""")]
    [InlineData("""{"c":311.55""")]
    public void Parse_UnusableBody_ReturnsLookupFailed(string? body)
    {
        FinnhubQuoteParser.Parse(body).Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    /// <summary>
    /// The chat uses Stooq-style tickers; Finnhub names US listings without a suffix. Other markets keep
    /// theirs, which is the form Finnhub uses for them.
    /// </summary>
    [Theory]
    [InlineData("aapl.us", "AAPL")]
    [InlineData("msft.us", "MSFT")]
    [InlineData("shop.to", "SHOP.TO")]
    [InlineData("usdbrl", "USDBRL")]
    public void ToSymbol_ChatTicker_BecomesTheSymbolFinnhubExpects(string ticker, string expected)
    {
        FinnhubQuoteParser.ToSymbol(Code(ticker)).Should().Be(expected);
    }

    /// <summary>
    /// The price is parsed with invariant culture. Without it a de-DE host would read 311.55 as 31155 and
    /// post a wildly wrong quote — the same rule the Stooq parser follows.
    /// </summary>
    [Fact]
    public void Parse_CommaDecimalCulture_StillReadsTheDotAsADecimalPoint()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            311.55m.ToString(CultureInfo.CurrentCulture).Should().Contain(",", "the test culture must disagree");

            FinnhubQuoteParser.Parse(QuoteBody).Price.Should().Be(311.55m);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static StockCode Code(string value)
    {
        Result<StockCode> code = StockCode.Create(value);
        code.IsSuccess.Should().BeTrue();

        return code.Value;
    }
}
