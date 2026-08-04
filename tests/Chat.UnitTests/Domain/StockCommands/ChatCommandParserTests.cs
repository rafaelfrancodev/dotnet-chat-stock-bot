using System.Globalization;
using Chat.Domain.StockCommands;

namespace Chat.UnitTests.Domain.StockCommands;

public sealed class ChatCommandParserTests
{
    // Built from code points so the source file carries no invisible characters.
    private const char ZeroWidthSpace = (char)0x200B;

    private static readonly string NulCharacter = ((char)0).ToString();

    [Fact]
    public void Parse_StockCommand_ReturnsStockQuote()
    {
        ParsedChatInput parsed = ChatCommandParser.Parse("/stock=aapl.us");

        parsed.Should().BeOfType<ParsedChatInput.StockQuote>()
            .Which.Code.Value.Should().Be("aapl.us");
    }

    [Theory]
    [InlineData("/STOCK=AAPL.US")]
    [InlineData("/Stock=Aapl.Us")]
    [InlineData("/sToCk=aApL.uS")]
    public void Parse_UpperCaseStockCommand_ReturnsStockQuote(string input)
    {
        ParsedChatInput parsed = ChatCommandParser.Parse(input);

        parsed.Should().BeOfType<ParsedChatInput.StockQuote>()
            .Which.Code.Value.Should().Be("aapl.us");
    }

    [Theory]
    [InlineData("  /stock=aapl.us  ")]
    [InlineData("/stock= aapl.us")]
    [InlineData("\t/stock =aapl.us\n")]
    public void Parse_WhitespacePaddedStockCommand_ReturnsStockQuote(string input)
    {
        ParsedChatInput parsed = ChatCommandParser.Parse(input);

        parsed.Should().BeOfType<ParsedChatInput.StockQuote>()
            .Which.Code.Value.Should().Be("aapl.us");
    }

    [Fact]
    public void Parse_TurkishCulture_StillMatchesCommandName()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

        try
        {
            ParsedChatInput parsed = ChatCommandParser.Parse("/STOCK=AAPL.US");

            parsed.Should().BeOfType<ParsedChatInput.StockQuote>()
                .Which.Code.Value.Should().Be("aapl.us");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("/stock=")]
    [InlineData("/stock=   ")]
    [InlineData("/STOCK=")]
    public void Parse_StockCommandWithoutCode_ReturnsInvalid(string input)
    {
        ParsedChatInput parsed = ChatCommandParser.Parse(input);

        parsed.Should().BeOfType<ParsedChatInput.Invalid>()
            .Which.Error.Should().Be(StockCode.Errors.Empty);
    }

    [Theory]
    [InlineData("/stock=a&b")]
    [InlineData("/stock==")]
    [InlineData("/stock=aapl us")]
    [InlineData("/stock=<script>")]
    public void Parse_StockCommandWithInvalidCode_ReturnsInvalidWithStockCodeError(string input)
    {
        ParsedChatInput parsed = ChatCommandParser.Parse(input);

        parsed.Should().BeOfType<ParsedChatInput.Invalid>()
            .Which.Error.Should().Be(StockCode.Errors.InvalidFormat);
    }

    [Fact]
    public void Parse_StockCommandWithTooLongCode_ReturnsInvalidWithStockCodeError()
    {
        string input = $"/stock={new string('a', StockCode.MaxLength + 1)}";

        ParsedChatInput parsed = ChatCommandParser.Parse(input);

        parsed.Should().BeOfType<ParsedChatInput.Invalid>()
            .Which.Error.Should().Be(StockCode.Errors.TooLong);
    }

    [Theory]
    [InlineData("/help", "help")]
    [InlineData("/HELP", "help")]
    [InlineData("  /Help  ", "help")]
    [InlineData("/help=me", "help")]
    public void Parse_UnknownSlashCommand_ReturnsUnknownCommand(string input, string expectedName)
    {
        ParsedChatInput parsed = ChatCommandParser.Parse(input);

        parsed.Should().BeOfType<ParsedChatInput.UnknownCommand>()
            .Which.CommandName.Should().Be(expectedName);
    }

    [Theory]
    [InlineData("/stock")]
    [InlineData("/STOCK")]
    [InlineData("  /stock  ")]
    public void Parse_StockCommandWithoutSeparator_ReturnsUnknownCommand(string input)
    {
        ParsedChatInput parsed = ChatCommandParser.Parse(input);

        parsed.Should().BeOfType<ParsedChatInput.UnknownCommand>()
            .Which.CommandName.Should().Be(ChatCommandParser.StockCommandName);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("   /   ")]
    [InlineData("/=")]
    [InlineData("/=aapl.us")]
    public void Parse_SlashWithoutCommandName_ReturnsInvalid(string input)
    {
        ParsedChatInput parsed = ChatCommandParser.Parse(input);

        parsed.Should().BeOfType<ParsedChatInput.Invalid>()
            .Which.Error.Should().Be(ChatCommandParser.Errors.MissingCommandName);
    }

    [Theory]
    [InlineData("hello there")]
    [InlineData("100% sure")]
    [InlineData("what does stock=aapl.us do?")]
    public void Parse_PlainText_ReturnsPlainMessage(string input)
    {
        ParsedChatInput parsed = ChatCommandParser.Parse(input);

        parsed.Should().BeOfType<ParsedChatInput.PlainMessage>()
            .Which.Text.Should().Be(input);
    }

    [Fact]
    public void Parse_PlainTextContainingStockCommand_ReturnsPlainMessage()
    {
        const string input = "try /stock=aapl.us to get a quote";

        ParsedChatInput parsed = ChatCommandParser.Parse(input);

        parsed.Should().BeOfType<ParsedChatInput.PlainMessage>()
            .Which.Text.Should().Be(input);
    }

    [Fact]
    public void Parse_WhitespacePaddedPlainText_TrimsTheText()
    {
        ParsedChatInput parsed = ChatCommandParser.Parse("  hello there \n");

        parsed.Should().BeOfType<ParsedChatInput.PlainMessage>()
            .Which.Text.Should().Be("hello there");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public void Parse_NullOrWhitespace_ReturnsEmptyPlainMessage(string? input)
    {
        ParsedChatInput parsed = ChatCommandParser.Parse(input);

        parsed.Should().BeOfType<ParsedChatInput.PlainMessage>()
            .Which.Text.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(GarbageInputs))]
    public void Parse_GarbageInput_DoesNotThrow(string? input)
    {
        ParsedChatInput? parsed = null;

        Action parse = () => parsed = ChatCommandParser.Parse(input);

        parse.Should().NotThrow();
        parsed.Should().NotBeNull();
    }

    public static TheoryData<string?> GarbageInputs() =>
        new()
        {
            null,
            string.Empty,
            "   ",
            "/",
            "//",
            "///stock=aapl.us",
            "/=",
            "/stock==",
            "/stock= ",
            "/stock=/../../etc/passwd",
            "/stock=aapl.us\r\nHost: evil",
            "=stock",
            NulCharacter,
            $"/{NulCharacter}stock=aapl.us",
            new string((char)1, 3),
            $"/{ZeroWidthSpace}=aapl.us",
            "🚀 /stock=aapl.us",
            "/stock=🚀",
            new string('x', 10_000),
            $"/{new string('x', 10_000)}",
            $"/stock={new string('x', 10_000)}",
        };
}
