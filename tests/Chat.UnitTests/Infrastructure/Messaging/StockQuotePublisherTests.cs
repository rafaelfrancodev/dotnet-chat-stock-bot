using Chat.Application.Abstractions.Stocks;
using Chat.Application.Contracts.Messaging;
using Chat.Infrastructure.Messaging;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Chat.UnitTests.Infrastructure.Messaging;

/// <summary>
/// The two outbound adapters, exercised over MassTransit's in-memory test harness. The harness runs a
/// real bus with a real serializer and no broker, so these tests stay hermetic while still proving the
/// message leaves through the transport rather than through a hand-written fake.
/// </summary>
public sealed class StockQuotePublisherTests
{
    private static readonly StockQuoteRequested Request = new(
        RequestId: Guid.CreateVersion7(),
        ChatRoomId: Guid.CreateVersion7(),
        StockCode: "aapl.us",
        RequestedByUserId: "user-42",
        RequestedByDisplayName: "Ada",
        RequestedAtUtc: new DateTimeOffset(2026, 8, 4, 21, 15, 30, TimeSpan.Zero));

    private static readonly StockQuoteResolved Response = new(
        RequestId: Request.RequestId,
        ChatRoomId: Request.ChatRoomId,
        StockCode: "aapl.us",
        RequestedByUserId: Request.RequestedByUserId,
        Outcome: StockQuoteOutcome.Quoted,
        Price: 93.42m,
        Message: "AAPL.US quote is $93.42 per share",
        ResolvedAtUtc: new DateTimeOffset(2026, 8, 4, 21, 15, 31, TimeSpan.Zero));

    [Fact]
    public async Task Publish_StockQuoteRequested_IsSentToBus()
    {
        await using TestBus bus = await TestBus.StartAsync();

        await bus.Resolve<IStockQuoteRequester>().RequestAsync(Request, CancellationToken.None);

        (await bus.Harness.Published.Any<StockQuoteRequested>()).Should().BeTrue();
    }

    [Fact]
    public async Task Publish_StockQuoteResolved_IsSentToBus()
    {
        await using TestBus bus = await TestBus.StartAsync();

        await bus.Resolve<IStockQuoteResponder>().RespondAsync(Response, CancellationToken.None);

        (await bus.Harness.Published.Any<StockQuoteResolved>()).Should().BeTrue();
    }

    /// <summary>
    /// The bot reads every member of this record, so the body must arrive intact — not a re-shaped
    /// projection the publisher invented on the way out.
    /// </summary>
    [Fact]
    public async Task Publish_StockQuoteRequested_CarriesTheContractUnchanged()
    {
        await using TestBus bus = await TestBus.StartAsync();

        await bus.Resolve<IStockQuoteRequester>().RequestAsync(Request, CancellationToken.None);

        (await bus.Harness.Published.Any<StockQuoteRequested>()).Should().BeTrue();
        bus.Harness.Published.Select<StockQuoteRequested>().Single()
            .Context.Message.Should().Be(Request);
    }

    [Fact]
    public async Task Publish_StockQuoteResolved_CarriesTheContractUnchanged()
    {
        await using TestBus bus = await TestBus.StartAsync();

        await bus.Resolve<IStockQuoteResponder>().RespondAsync(Response, CancellationToken.None);

        (await bus.Harness.Published.Any<StockQuoteResolved>()).Should().BeTrue();
        bus.Harness.Published.Select<StockQuoteResolved>().Single()
            .Context.Message.Should().Be(Response);
    }

    /// <summary>
    /// Publishing by message type — rather than sending to a queue address — is what lets the topology
    /// add a subscriber without touching the producer. A regression to <c>Send</c> would still deliver
    /// today and quietly remove that property, so it is asserted.
    /// </summary>
    [Fact]
    public async Task Publish_StockQuoteRequested_IsPublishedNotSentToAQueue()
    {
        await using TestBus bus = await TestBus.StartAsync();

        await bus.Resolve<IStockQuoteRequester>().RequestAsync(Request, CancellationToken.None);

        (await bus.Harness.Published.Any<StockQuoteRequested>()).Should().BeTrue();
        bus.Harness.Sent.Select<StockQuoteRequested>().Should().BeEmpty();
    }

    /// <summary>
    /// A publish is I/O: a disconnecting client or a stopping host must be able to abandon it, so the
    /// caller's token has to reach the transport rather than being dropped at the adapter.
    /// </summary>
    [Fact]
    public async Task RequestAsync_Always_ForwardsTheCallersCancellationToken()
    {
        IPublishEndpoint publishEndpoint = Substitute.For<IPublishEndpoint>();
        using CancellationTokenSource cancellation = new();
        MassTransitStockQuoteRequester requester = new(publishEndpoint);

        await requester.RequestAsync(Request, cancellation.Token);

        await publishEndpoint.Received(1).Publish(Request, cancellation.Token);
    }

    [Fact]
    public async Task RespondAsync_Always_ForwardsTheCallersCancellationToken()
    {
        IPublishEndpoint publishEndpoint = Substitute.For<IPublishEndpoint>();
        using CancellationTokenSource cancellation = new();
        MassTransitStockQuoteResponder responder = new(publishEndpoint);

        await responder.RespondAsync(Response, cancellation.Token);

        await publishEndpoint.Received(1).Publish(Response, cancellation.Token);
    }

    [Fact]
    public async Task RequestAsync_NullRequest_Throws()
    {
        MassTransitStockQuoteRequester requester = new(Substitute.For<IPublishEndpoint>());

        await FluentActions.Awaiting(() => requester.RequestAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RespondAsync_NullResponse_Throws()
    {
        MassTransitStockQuoteResponder responder = new(Substitute.For<IPublishEndpoint>());

        await FluentActions.Awaiting(() => responder.RespondAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// A started in-memory bus with the two production adapters registered against it, disposed with
    /// the test so no harness outlives its scope.
    /// </summary>
    private sealed class TestBus : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private TestBus(ServiceProvider provider)
        {
            _provider = provider;
            _scope = provider.CreateAsyncScope();
            Harness = provider.GetRequiredService<ITestHarness>();
        }

        public ITestHarness Harness { get; }

        public static async Task<TestBus> StartAsync()
        {
            ServiceProvider provider = new ServiceCollection()
                .AddMassTransitTestHarness()
                .AddScoped<IStockQuoteRequester, MassTransitStockQuoteRequester>()
                .AddScoped<IStockQuoteResponder, MassTransitStockQuoteResponder>()
                .BuildServiceProvider(validateScopes: true);

            TestBus bus = new(provider);
            await bus.Harness.Start();

            return bus;
        }

        public T Resolve<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }
}
