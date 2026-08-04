using System.Globalization;
using Chat.Domain.Common;
using Chat.Domain.StockCommands;

namespace Chat.UnitTests.Domain.StockCommands;

public sealed class StockCodeTests
{
    [Theory]
    [InlineData("AAPL.US")]
    [InlineData("Aapl.Us")]
    [InlineData("  aApL.uS  ")]
    public void Create_MixedCase_NormalisesToLowerCase(string input)
    {
        Result<StockCode> result = StockCode.Create(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be("aapl.us");
    }

    [Fact]
    public void Create_TurkishCulture_StillNormalisesWithInvariantCasing()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

        try
        {
            Result<StockCode> result = StockCode.Create("AAPL.UI");

            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("aapl.ui");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Create_Empty_ReturnsFailure()
    {
        Result<StockCode> result = StockCode.Create(string.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StockCode.Errors.Empty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("\t\n  ")]
    public void Create_NullOrWhitespaceOnly_ReturnsFailure(string? input)
    {
        Result<StockCode> result = StockCode.Create(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StockCode.Errors.Empty);
    }

    [Fact]
    public void Create_TooLong_ReturnsFailure()
    {
        string tooLong = new('a', StockCode.MaxLength + 1);

        Result<StockCode> result = StockCode.Create(tooLong);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StockCode.Errors.TooLong);
    }

    [Fact]
    public void Create_ExactlyMaxLength_Succeeds()
    {
        string atLimit = new('a', StockCode.MaxLength);

        Result<StockCode> result = StockCode.Create(atLimit);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().HaveLength(StockCode.MaxLength);
    }

    [Theory]
    [InlineData("aapl.us&f=sd2t2ohlcv")]
    [InlineData("aapl.us?s=msft.us")]
    [InlineData("aapl.us/../q")]
    [InlineData("s=aapl.us")]
    [InlineData("aapl%2eus")]
    [InlineData("aapl.us#fragment")]
    [InlineData("aapl us")]
    [InlineData("aapl\\us")]
    [InlineData("aapl.us\rHost: evil")]
    [InlineData("aapl+us")]
    [InlineData("<script>")]
    public void Create_ContainsUrlInjectionCharacters_ReturnsFailure(string input)
    {
        Result<StockCode> result = StockCode.Create(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StockCode.Errors.InvalidFormat);
    }

    [Theory]
    [InlineData("aapl.us")]
    [InlineData("brk-b.us")]
    [InlineData("msft")]
    [InlineData("11b.pl")]
    public void Create_ValidTicker_Succeeds(string input)
    {
        Result<StockCode> result = StockCode.Create(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(input);
    }

    [Fact]
    public void Display_ValidCode_ReturnsUpperCase()
    {
        StockCode code = StockCode.Create("aapl.us").Value;

        code.Display.Should().Be("AAPL.US");
    }

    [Fact]
    public void ToString_ValidCode_ReturnsNormalisedValue()
    {
        StockCode code = StockCode.Create(" AAPL.US ").Value;

        code.ToString().Should().Be("aapl.us");
    }

    [Fact]
    public void Equality_SameCodeDifferentCasing_AreEqual()
    {
        StockCode first = StockCode.Create("AAPL.US").Value;
        StockCode second = StockCode.Create("  aapl.us ").Value;

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentCode_AreNotEqual()
    {
        StockCode first = StockCode.Create("aapl.us").Value;
        StockCode second = StockCode.Create("msft.us").Value;

        first.Should().NotBe(second);
    }
}
