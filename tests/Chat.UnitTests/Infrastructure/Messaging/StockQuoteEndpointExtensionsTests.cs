using Chat.Application.Contracts.Messaging;
using Chat.Infrastructure.Messaging;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.UnitTests.Infrastructure.Messaging;

/// <summary>
/// Proves the receive endpoints a host registers really are the queues the topology documents. The
/// assertion is the address the message was received on, not the registration call, so a change in
/// MassTransit's naming conventions would surface here rather than as an empty queue in production.
/// </summary>
public sealed class StockQuoteEndpointExtensionsTests
{
    [Fact]
    public async Task AddStockQuoteRequestConsumer_BindsTheConsumerToTheRequestEndpoint()
    {
        await using TestEndpoint endpoint = await TestEndpoint.StartAsync(
            configurator => configurator.AddStockQuoteRequestConsumer<RecordingRequestConsumer>());

        await endpoint.Harness.Bus.Publish(AnyRequest);

        (await endpoint.Harness.Consumed.Any<StockQuoteRequested>()).Should().BeTrue();
        InputQueueOf(endpoint.Harness.Consumed.Select<StockQuoteRequested>().Single())
            .Should().Be(MessagingConstants.StockQuoteRequestQueue);
    }

    [Fact]
    public async Task AddStockQuoteResponseConsumer_BindsTheConsumerToTheResponseEndpoint()
    {
        await using TestEndpoint endpoint = await TestEndpoint.StartAsync(
            configurator => configurator.AddStockQuoteResponseConsumer<RecordingResponseConsumer>());

        await endpoint.Harness.Bus.Publish(AnyResponse);

        (await endpoint.Harness.Consumed.Any<StockQuoteResolved>()).Should().BeTrue();
        InputQueueOf(endpoint.Harness.Consumed.Select<StockQuoteResolved>().Single())
            .Should().Be(MessagingConstants.StockQuoteResponseQueue);
    }

    /// <summary>
    /// Without the explicit name MassTransit would derive one from the consumer's class name, so the
    /// queue would silently move on a rename. Asserting the two differ keeps the extension load-bearing
    /// instead of a decorative wrapper around the default convention.
    /// </summary>
    [Theory]
    [InlineData(MessagingConstants.StockQuoteRequestQueue)]
    [InlineData(MessagingConstants.StockQuoteResponseQueue)]
    public void EndpointNames_ComeFromMessagingConstants_NotFromTheConsumerClassName(string endpointName)
    {
        string[] conventionDerived =
        [
            KebabCaseEndpointNameFormatter.Instance.Consumer<RecordingRequestConsumer>(),
            KebabCaseEndpointNameFormatter.Instance.Consumer<RecordingResponseConsumer>(),
        ];

        conventionDerived.Should().NotContain(endpointName);
    }

    [Fact]
    public void AddStockQuoteRequestConsumer_NullConfigurator_Throws()
    {
        Action registering = () =>
            StockQuoteEndpointExtensions.AddStockQuoteRequestConsumer<RecordingRequestConsumer>(null!);

        registering.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddStockQuoteResponseConsumer_NullConfigurator_Throws()
    {
        Action registering = () =>
            StockQuoteEndpointExtensions.AddStockQuoteResponseConsumer<RecordingResponseConsumer>(null!);

        registering.Should().Throw<ArgumentNullException>();
    }

    private static StockQuoteRequested AnyRequest => new(
        RequestId: Guid.CreateVersion7(),
        ChatRoomId: Guid.CreateVersion7(),
        StockCode: "aapl.us",
        RequestedByUserId: "user-42",
        RequestedByDisplayName: "Ada",
        RequestedAtUtc: new DateTimeOffset(2026, 8, 4, 21, 15, 30, TimeSpan.Zero));

    private static StockQuoteResolved AnyResponse => new(
        RequestId: Guid.CreateVersion7(),
        ChatRoomId: Guid.CreateVersion7(),
        StockCode: "aapl.us",
        RequestedByUserId: "user-42",
        Outcome: StockQuoteOutcome.Quoted,
        Price: 93.42m,
        Message: "AAPL.US quote is $93.42 per share",
        ResolvedAtUtc: new DateTimeOffset(2026, 8, 4, 21, 15, 31, TimeSpan.Zero));

    private static string InputQueueOf<T>(IReceivedMessage<T> message) where T : class =>
        message.Context.ReceiveContext.InputAddress.AbsolutePath.Split('/')[^1];

    /// <summary>Stands in for task 1.15's consumer: this task only pins where it listens.</summary>
    private sealed class RecordingRequestConsumer : IConsumer<StockQuoteRequested>
    {
        public Task Consume(ConsumeContext<StockQuoteRequested> context) => Task.CompletedTask;
    }

    /// <summary>Stands in for task 1.16's consumer.</summary>
    private sealed class RecordingResponseConsumer : IConsumer<StockQuoteResolved>
    {
        public Task Consume(ConsumeContext<StockQuoteResolved> context) => Task.CompletedTask;
    }

    /// <summary>A started in-memory bus carrying whatever consumer the test registered.</summary>
    private sealed class TestEndpoint(ServiceProvider provider) : IAsyncDisposable
    {
        public ITestHarness Harness { get; } = provider.GetRequiredService<ITestHarness>();

        public static async Task<TestEndpoint> StartAsync(Action<IBusRegistrationConfigurator> register)
        {
            ServiceProvider provider = new ServiceCollection()
                .AddMassTransitTestHarness(register)
                .BuildServiceProvider(validateScopes: true);

            TestEndpoint endpoint = new(provider);
            await endpoint.Harness.Start();

            return endpoint;
        }

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
