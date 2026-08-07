using Chat.Application.Contracts.Messaging;
using Chat.Application.Features.StockCommands.ResolveStockQuote;
using Chat.Bot;
using Chat.Domain.Common;
using Chat.Infrastructure.Messaging;
using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Chat.UnitTests.Bot;

/// <summary>
/// The bot's inbound adapter. There is no <c>BackgroundService</c> to test: MassTransit's bus is the
/// hosted service, and this class is everything the bot adds on top of it — so what is worth pinning is
/// the mapping from the wire contract onto the use case, and that a payload it cannot map neither crashes
/// the consumer nor reaches Stooq.
/// </summary>
public sealed class StockQuoteRequestConsumerTests
{
    private const string UserId = "user-42";

    private static readonly Guid RequestId = Guid.Parse("0198cd4d-0000-7000-8000-00000000abcd");
    private static readonly Guid RoomId = Guid.Parse("0198cd4d-1111-7000-8000-00000000abcd");

    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly List<ResolveStockQuoteCommand> _dispatched = [];
    private readonly List<CancellationToken> _dispatchTokens = [];

    public StockQuoteRequestConsumerTests()
    {
        _sender
            .Send(Arg.Any<ResolveStockQuoteCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _dispatched.Add(call.Arg<ResolveStockQuoteCommand>()!);
                _dispatchTokens.Add(call.Arg<CancellationToken>());

                return Result.Success();
            });
    }

    [Fact]
    public async Task Consume_StockQuoteRequested_DispatchesTheResolveCommand()
    {
        await ConsumeAsync(Request());

        ResolveStockQuoteCommand command = DispatchedCommand();
        command.RequestId.Should().Be(RequestId, "both hops must share the correlation id");
        command.ChatRoomId.Value.Should().Be(RoomId);
        command.StockCode.Value.Should().Be("aapl.us");
        command.RequestedByUserId.Should().Be(UserId);
    }

    /// <summary>
    /// The wire carries a string, so the value object is rebuilt at the boundary rather than trusted.
    /// That is what keeps an unnormalised ticker out of the outbound Stooq URL even if some other
    /// publisher appears.
    /// </summary>
    [Theory]
    [InlineData("AAPL.US", "aapl.us")]
    [InlineData("  msft.us  ", "msft.us")]
    public async Task Consume_UnnormalisedStockCode_IsRebuiltThroughTheValueObject(string wire, string expected)
    {
        await ConsumeAsync(Request(stockCode: wire));

        DispatchedCommand().StockCode.Value.Should().Be(expected);
    }

    /// <summary>
    /// Only Chat.Web publishes here and it can only hold a validated code, so an unusable ticker means a
    /// hand-crafted message with no participant waiting on it. Retrying would reproduce the same rejection
    /// four times and then dead-letter something already understood, so it is logged and acknowledged.
    /// <para>
    /// Dispatching nothing also means <b>answering nothing</b>, which is deliberate rather than the one hole
    /// in "the room always gets an answer": posting a quote failure into a room that never asked would be
    /// noise a forged message could aim anywhere.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("aapl.us&f=x")]
    [InlineData("../../etc/passwd")]
    [InlineData("this-ticker-is-far-too-long-to-be-real")]
    public async Task Consume_UnusableStockCode_DispatchesNothingAndDoesNotFault(string stockCode)
    {
        Func<Task> act = () => ConsumeAsync(Request(stockCode: stockCode));

        await act.Should().NotThrowAsync();
        _dispatched.Should().BeEmpty("an invalid ticker must never reach the quote provider");
    }

    [Fact]
    public async Task Consume_Always_ForwardsTheConsumeCancellationToken()
    {
        using CancellationTokenSource cancellation = new();

        await ConsumeAsync(Request(), cancellation.Token);

        _dispatchTokens.Should().ContainSingle().Which.Should().Be(cancellation.Token);
    }

    /// <summary>
    /// A failed <see cref="Result"/> is an expected, deterministic failure: an identical redelivery would
    /// reproduce it. It is logged and acknowledged rather than turned into an exception, which is what the
    /// Result pattern exists to avoid.
    /// <para>
    /// Only a pipeline behaviour can produce this, since the handler always succeeds — and no validator is
    /// registered for <c>ResolveStockQuoteCommand</c>, so nothing reaches it today. Registering one would
    /// turn this into a legitimate request left unanswered, which is why the consumer logs it at Error.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Consume_DispatchFails_AcknowledgesInsteadOfFaultingTheDelivery()
    {
        _sender
            .Send(Arg.Any<ResolveStockQuoteCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Validation("Test.Rejected", "rejected")));

        Func<Task> act = () => ConsumeAsync(Request());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_NullContext_Throws()
    {
        Func<Task> act = () => CreateConsumer().Consume(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// End to end over MassTransit's in-memory harness — no broker: the consumer really is bound to
    /// <see cref="MessagingConstants.StockQuoteRequestQueue"/>, really receives a published
    /// <see cref="StockQuoteRequested"/>, and dispatches the use case without faulting the delivery.
    /// </summary>
    [Fact]
    public async Task Consume_PublishedRequest_ReachesTheConsumerOnTheRequestQueue()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_sender)
            .AddMassTransitTestHarness(configurator =>
                configurator.AddStockQuoteRequestConsumer<StockQuoteRequestConsumer>())
            .BuildServiceProvider(validateScopes: true);

        ITestHarness harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(Request());

        (await harness.Consumed.Any<StockQuoteRequested>()).Should().BeTrue();

        IReceivedMessage<StockQuoteRequested> received =
            harness.Consumed.Select<StockQuoteRequested>().Single();
        received.Exception.Should().BeNull("a well-formed request must not fault the delivery");
        received.Context.ReceiveContext.InputAddress.AbsolutePath.Split('/')[^1]
            .Should().Be(MessagingConstants.StockQuoteRequestQueue);

        DispatchedCommand().StockCode.Value.Should().Be("aapl.us");
    }

    private StockQuoteRequestConsumer CreateConsumer() =>
        new(_sender, NullLogger<StockQuoteRequestConsumer>.Instance);

    private Task ConsumeAsync(StockQuoteRequested request, CancellationToken cancellationToken = default)
    {
        ConsumeContext<StockQuoteRequested> context = Substitute.For<ConsumeContext<StockQuoteRequested>>();
        context.Message.Returns(request);
        context.CancellationToken.Returns(cancellationToken);

        return CreateConsumer().Consume(context);
    }

    private ResolveStockQuoteCommand DispatchedCommand()
    {
        _dispatched.Should().HaveCount(1, "one delivery must produce exactly one use-case dispatch");

        return _dispatched[0];
    }

    private static StockQuoteRequested Request(string stockCode = "aapl.us") => new(
        RequestId: RequestId,
        ChatRoomId: RoomId,
        StockCode: stockCode,
        RequestedByUserId: UserId,
        RequestedByDisplayName: "Ada",
        RequestedAtUtc: new DateTimeOffset(2026, 8, 5, 18, 45, 0, TimeSpan.Zero));
}
