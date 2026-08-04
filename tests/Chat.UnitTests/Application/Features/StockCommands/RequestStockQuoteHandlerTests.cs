using System.Reflection;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Stocks;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Messaging;
using Chat.Application.Features.StockCommands.RequestStockQuote;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.StockCommands;
using NSubstitute;

namespace Chat.UnitTests.Application.Features.StockCommands;

public sealed class RequestStockQuoteHandlerTests
{
    private const string UserId = "user-1";
    private const string DisplayName = "Alice";

    private static readonly ChatRoomId RoomId = ChatRoomId.New();
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 30, 0, TimeSpan.Zero);

    private readonly IStockQuoteRequester _stockQuotes = Substitute.For<IStockQuoteRequester>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public RequestStockQuoteHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    private RequestStockQuoteHandler CreateHandler() => new(_stockQuotes, _clock);

    private static RequestStockQuoteCommand Command(string stockCode = "aapl.us") =>
        new(RoomId, StockCode.Create(stockCode).Value, UserId, DisplayName);

    [Fact]
    public async Task Handle_ValidCommand_PublishesTheRequestAndSucceeds()
    {
        Result result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _stockQuotes.Received(1).RequestAsync(Arg.Any<StockQuoteRequested>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("aapl.us")]
    [InlineData("msft.us")]
    public async Task Handle_ValidCommand_PublishesTheParsedStockCode(string stockCode)
    {
        await CreateHandler().Handle(Command(stockCode), CancellationToken.None);

        await _stockQuotes.Received(1).RequestAsync(
            Arg.Is<StockQuoteRequested>(request => request!.StockCode == stockCode),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_CarriesTheRoomAndTheRequesterIdentity()
    {
        await CreateHandler().Handle(Command(), CancellationToken.None);

        await _stockQuotes.Received(1).RequestAsync(
            Arg.Is<StockQuoteRequested>(request =>
                request!.ChatRoomId == RoomId.Value
                && request.RequestedByUserId == UserId
                && request.RequestedByDisplayName == DisplayName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_StampsTheRequestWithTheInjectedClock()
    {
        // Never DateTimeOffset.UtcNow read inline: the instant has to be the application clock's, or the
        // request the bot answers cannot be correlated with the post it produces.
        await CreateHandler().Handle(Command(), CancellationToken.None);

        await _stockQuotes.Received(1).RequestAsync(
            Arg.Is<StockQuoteRequested>(request => request!.RequestedAtUtc == Now),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TwoCommands_GetDistinctRequestIds()
    {
        List<Guid> requestIds = [];
        _stockQuotes
            .When(requester => requester.RequestAsync(Arg.Any<StockQuoteRequested>(), Arg.Any<CancellationToken>()))
            .Do(call => requestIds.Add(call.Arg<StockQuoteRequested>()!.RequestId));

        await CreateHandler().Handle(Command(), CancellationToken.None);
        await CreateHandler().Handle(Command(), CancellationToken.None);

        requestIds.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        requestIds.Should().NotContain(Guid.Empty);
    }

    [Fact]
    public async Task Handle_Always_ForwardsTheCancellationToken()
    {
        using CancellationTokenSource cancellation = new();

        await CreateHandler().Handle(Command(), cancellation.Token);

        await _stockQuotes.Received(1).RequestAsync(Arg.Any<StockQuoteRequested>(), cancellation.Token);
    }

    [Fact]
    public async Task Handle_NullCommand_Throws()
    {
        Func<Task> act = () => CreateHandler().Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_TakesNoPersistenceDependency_SoAStockCommandCanNeverBeWritten()
    {
        // The challenge's hard constraint, enforced by the build rather than by review: this handler is the
        // whole of the /stock= code path, and it is not given anything that could write a row.
        string persistence = typeof(IUnitOfWork).Namespace!;

        ParameterInfo[] parameters = [.. typeof(RequestStockQuoteHandler)
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())];

        parameters.Should().NotBeEmpty();
        parameters.Should().NotContain(parameter => parameter.ParameterType.Namespace == persistence);
        parameters.Select(parameter => parameter.ParameterType)
            .Should().NotContain([typeof(IMessageRepository), typeof(IChatRoomRepository), typeof(IUnitOfWork)]);
    }
}
