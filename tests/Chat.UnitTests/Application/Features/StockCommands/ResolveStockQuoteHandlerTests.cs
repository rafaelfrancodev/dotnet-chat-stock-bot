using System.Globalization;
using System.Reflection;
using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Stocks;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Messaging;
using Chat.Application.Features.StockCommands.ResolveStockQuote;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.StockCommands;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Chat.UnitTests.Application.Features.StockCommands;

/// <summary>
/// The bot's answer is the one string the challenge quotes verbatim, so these tests pin the wording
/// character for character and prove the room is answered on every path — including the one a reviewer
/// will actually see while Stooq's CSV endpoint answers 404.
/// </summary>
public sealed class ResolveStockQuoteHandlerTests
{
    private const string UserId = "user-1";

    private static readonly Guid RequestId = Guid.Parse("0198cd4d-0000-7000-8000-00000000abcd");
    private static readonly ChatRoomId RoomId = ChatRoomId.New();
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 18, 45, 0, TimeSpan.Zero);

    private readonly IStockQuoteProvider _stockQuotes = Substitute.For<IStockQuoteProvider>();
    private readonly IStockQuoteResponder _responder = Substitute.For<IStockQuoteResponder>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly List<StockQuoteResolved> _published = [];

    public ResolveStockQuoteHandlerTests()
    {
        _clock.UtcNow.Returns(Now);

        _responder
            .When(responder => responder.RespondAsync(Arg.Any<StockQuoteResolved>(), Arg.Any<CancellationToken>()))
            .Do(call => _published.Add(call.Arg<StockQuoteResolved>()!));
    }

    [Fact]
    public async Task Handle_ValidQuote_PublishesExpectedMessageFormat()
    {
        // The exact sentence from the challenge brief. Nothing about it is derived at run time except the
        // ticker and the price, both of which come from the request and the provider.
        GivenLookup(StockQuoteLookup.Quoted(93.42m));

        Result result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        PublishedAnswer().Message.Should().Be("AAPL.US quote is $93.42 per share");
    }

    [Theory]
    [InlineData("aapl.us", 93.42, "AAPL.US quote is $93.42 per share")]
    [InlineData("msft.us", 206.5, "MSFT.US quote is $206.50 per share")]
    [InlineData("cdr.pl", 1234, "CDR.PL quote is $1234.00 per share")]
    [InlineData("aapl.us", 0.5, "AAPL.US quote is $0.50 per share")]
    public async Task Handle_ValidQuote_RendersTwoDecimalsAndTheUpperCaseTicker(
        string stockCode,
        double price,
        string expected)
    {
        GivenLookup(StockQuoteLookup.Quoted((decimal)price));

        await CreateHandler().Handle(Command(stockCode), CancellationToken.None);

        PublishedAnswer().Message.Should().Be(expected);
    }

    /// <summary>
    /// The formatting rule the deliverable depends on: the bot runs as a service, its culture is whatever
    /// the machine says, and the challenge's wording uses a dot. The test asserts the chosen culture really
    /// disagrees first, so it cannot pass vacuously on an invariant-culture machine.
    /// </summary>
    [Fact]
    public async Task Handle_CommaDecimalCulture_StillFormatsThePriceWithADot()
    {
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            93.42m.ToString("F2", CultureInfo.CurrentCulture)
                .Should().Be("93,42", "the culture under test must disagree with the invariant one");

            GivenLookup(StockQuoteLookup.Quoted(93.42m));

            await CreateHandler().Handle(Command(), CancellationToken.None);

            PublishedAnswer().Message.Should().Be("AAPL.US quote is $93.42 per share");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task Handle_ValidQuote_PublishesTheOutcomeAndThePriceAlongsideTheText()
    {
        GivenLookup(StockQuoteLookup.Quoted(93.42m));

        await CreateHandler().Handle(Command(), CancellationToken.None);

        StockQuoteResolved answer = PublishedAnswer();
        answer.Outcome.Should().Be(StockQuoteOutcome.Quoted);
        answer.Price.Should().Be(93.42m);
    }

    [Fact]
    public async Task Handle_SymbolNotFound_PublishesFriendlyMessage()
    {
        GivenLookup(StockQuoteLookup.SymbolNotFound);

        Result result = await CreateHandler().Handle(Command("aapl.xx"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("an unknown ticker is an answer, not a failure of the use case");

        StockQuoteResolved answer = PublishedAnswer();
        answer.Outcome.Should().Be(StockQuoteOutcome.SymbolNotFound);
        answer.Price.Should().BeNull();
        answer.Message.Should().Be("Sorry, I could not find a quote for AAPL.XX.");
    }

    /// <summary>
    /// The line a reviewer is most likely to read, because Stooq's CSV endpoint has answered 404 since
    /// task 1.14. It names the ticker and blames the quote service, and it leaves the "try again shortly"
    /// instruction to <c>ChatAlert.QuoteServiceUnavailable</c> so the banner and the chat line complement
    /// each other instead of repeating.
    /// </summary>
    [Fact]
    public async Task Handle_LookupFailed_PublishesAFriendlyOutageMessage()
    {
        GivenLookup(StockQuoteLookup.LookupFailed);

        Result result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        StockQuoteResolved answer = PublishedAnswer();
        answer.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
        answer.Price.Should().BeNull();
        answer.Message.Should().Be(
            "I could not reach the quote service, so I have no price for AAPL.US right now.");
    }

    [Fact]
    public async Task Handle_ProviderThrows_PublishesLookupFailedAndDoesNotRethrow()
    {
        // IStockQuoteProvider promises never to throw for a Stooq problem, so this is a provider bug. The
        // room must still get an answer: a broken adapter cannot be allowed to turn into silence.
        _stockQuotes
            .GetQuoteAsync(Arg.Any<StockCode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the provider broke its contract"));

        Result result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        StockQuoteResolved answer = PublishedAnswer();
        answer.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
        answer.Message.Should().Be(
            "I could not reach the quote service, so I have no price for AAPL.US right now.");
    }

    /// <summary>
    /// A "quoted" outcome with no number is a contract violation the room must not see as "$0.00 per
    /// share". It is downgraded, so the outcome 1.16 reads to decide about the outage banner still agrees
    /// with the text the participant is shown.
    /// </summary>
    [Fact]
    public async Task Handle_QuotedWithoutAPrice_IsDowngradedToLookupFailed()
    {
        GivenLookup(new StockQuoteLookup(StockQuoteOutcome.Quoted, Price: null));

        Result result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        StockQuoteResolved answer = PublishedAnswer();
        answer.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
        answer.Price.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Always_EchoesTheCorrelationRoomTickerAndRequester()
    {
        GivenLookup(StockQuoteLookup.Quoted(93.42m));

        await CreateHandler().Handle(Command(), CancellationToken.None);

        StockQuoteResolved answer = PublishedAnswer();
        answer.RequestId.Should().Be(RequestId);
        answer.ChatRoomId.Should().Be(RoomId.Value);
        answer.StockCode.Should().Be("aapl.us", "the wire echo stays normalised; only the display form is upper case");
        answer.RequestedByUserId.Should().Be(UserId, "1.16 aims the outage banner at this participant alone");
    }

    [Fact]
    public async Task Handle_Always_StampsTheAnswerWithTheInjectedClock()
    {
        GivenLookup(StockQuoteLookup.Quoted(93.42m));

        await CreateHandler().Handle(Command(), CancellationToken.None);

        PublishedAnswer().ResolvedAtUtc.Should().Be(Now);
        _ = _clock.Received().UtcNow;
    }

    [Fact]
    public async Task Handle_Always_ForwardsTheCancellationTokenToTheLookupAndThePublish()
    {
        using CancellationTokenSource cancellation = new();
        GivenLookup(StockQuoteLookup.Quoted(93.42m));

        await CreateHandler().Handle(Command(), cancellation.Token);

        await _stockQuotes.Received(1).GetQuoteAsync(Arg.Any<StockCode>(), cancellation.Token);
        await _responder.Received(1).RespondAsync(Arg.Any<StockQuoteResolved>(), cancellation.Token);
    }

    /// <summary>
    /// A genuine cancellation is the bot shutting down or the delivery being abandoned. Answering it with
    /// "I could not reach the quote service" would post noise into a room that is no longer waiting, so it
    /// propagates and MassTransit decides what to do with the message.
    /// </summary>
    [Fact]
    public async Task Handle_CallerCancels_PropagatesTheCancellationWithoutPublishing()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        _stockQuotes
            .GetQuoteAsync(Arg.Any<StockCode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        Func<Task> act = () => CreateHandler().Handle(Command(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _responder.DidNotReceiveWithAnyArgs().RespondAsync(default!, default);
    }

    /// <summary>
    /// A refused publish is not an answer the bot can reword — it means the broker rejected the message —
    /// so it must reach MassTransit, which retries and finally dead-letters. Swallowing it would lose the
    /// request without a trace.
    /// </summary>
    [Fact]
    public async Task Handle_PublishFails_PropagatesSoTheDeliveryIsRetried()
    {
        GivenLookup(StockQuoteLookup.Quoted(93.42m));
        _responder
            .RespondAsync(Arg.Any<StockQuoteResolved>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("broker refused the publish"));

        Func<Task> act = () => CreateHandler().Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_Always_LooksUpTheValidatedCodeExactlyOnce()
    {
        GivenLookup(StockQuoteLookup.Quoted(93.42m));

        await CreateHandler().Handle(Command(), CancellationToken.None);

        await _stockQuotes.Received(1).GetQuoteAsync(
            Arg.Is<StockCode>(code => code!.Value == "aapl.us"),
            Arg.Any<CancellationToken>());
        await _responder.Received(1).RespondAsync(Arg.Any<StockQuoteResolved>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullCommand_Throws()
    {
        Func<Task> act = () => CreateHandler().Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Without the marker no host registers the handler and the consumer fails at run time with "no
    /// handler for request" — the exact defect task 1.10a existed to make impossible.
    /// </summary>
    [Fact]
    public void Handler_IsMarkedAsABotFeature_SoChatBotRegistersIt()
    {
        typeof(ResolveStockQuoteHandler).Should().Implement<IBotFeature>();
        typeof(ResolveStockQuoteHandler).Should().NotImplement<IWebFeature>();
    }

    [Fact]
    public void Constructor_TakesNoPersistenceDependency_SoTheBotStaysDecoupledFromTheDatabase()
    {
        // Chat.Bot never calls AddPersistence(). Asserted here as well as in HostFeatureTests, because
        // this is the first handler the bot actually runs and the guarantee is worth a local test.
        ParameterInfo[] parameters = [.. typeof(ResolveStockQuoteHandler)
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())];

        parameters.Should().NotBeEmpty();
        parameters.Should().NotContain(parameter => parameter.ParameterType.Namespace == typeof(IUnitOfWork).Namespace);
        parameters.Select(parameter => parameter.ParameterType)
            .Should().NotContain([typeof(IMessageRepository), typeof(IChatRoomRepository), typeof(IUnitOfWork)]);
    }

    private ResolveStockQuoteHandler CreateHandler() =>
        new(_stockQuotes, _responder, _clock, NullLogger<ResolveStockQuoteHandler>.Instance);

    private static ResolveStockQuoteCommand Command(string stockCode = "aapl.us") =>
        new(RequestId, RoomId, StockCode.Create(stockCode).Value, UserId);

    private void GivenLookup(StockQuoteLookup lookup) =>
        _stockQuotes.GetQuoteAsync(Arg.Any<StockCode>(), Arg.Any<CancellationToken>()).Returns(lookup);

    /// <summary>The single answer the bot published; the room must never be left without exactly one.</summary>
    private StockQuoteResolved PublishedAnswer()
    {
        _published.Should().HaveCount(1, "the bot answers every request exactly once");

        return _published[0];
    }
}
