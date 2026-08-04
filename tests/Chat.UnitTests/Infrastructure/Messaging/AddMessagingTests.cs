using Chat.Application.Abstractions.Stocks;
using Chat.Infrastructure;
using Chat.Infrastructure.Messaging;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Chat.UnitTests.Infrastructure.Messaging;

/// <summary>
/// Covers what a host gets from <c>AddMessaging</c>. Nothing here opens a connection: MassTransit builds
/// the bus lazily and only the hosted service started by a running host dials the broker.
/// </summary>
public sealed class AddMessagingTests
{
    [Theory]
    [InlineData(typeof(IStockQuoteRequester), typeof(MassTransitStockQuoteRequester))]
    [InlineData(typeof(IStockQuoteResponder), typeof(MassTransitStockQuoteResponder))]
    public async Task AddMessaging_ResolvesTheOutboundStockQuotePorts(Type port, Type implementation)
    {
        await using ServiceProvider provider = Configured();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService(port).Should().BeOfType(implementation);
    }

    /// <summary>
    /// Scoped, like MassTransit's own <see cref="IPublishEndpoint"/>: a singleton adapter would capture
    /// a scoped dependency, which scope validation rejects and which would drop the consume context that
    /// correlates the request with its answer.
    /// </summary>
    [Theory]
    [InlineData(typeof(IStockQuoteRequester))]
    [InlineData(typeof(IStockQuoteResponder))]
    public void AddMessaging_RegistersTheOutboundPortsAsScoped(Type port)
    {
        ServiceCollection services = [];
        services.AddMessaging(Configuration());

        services.Single(descriptor => descriptor.ServiceType == port)
            .Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task AddMessaging_BindsTheBrokerSettingsFromConfiguration()
    {
        await using ServiceProvider provider = Configured();

        RabbitMqOptions options = provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        options.HostName.Should().Be("127.0.0.1");
        options.Port.Should().Be(5672);
        options.VirtualHost.Should().Be("/");
        options.UserName.Should().Be("configured-user");
        options.Password.Should().Be("configured-password");
    }

    /// <summary>
    /// The defaults must stay unusable: credentials arrive from user-secrets or the environment, so a
    /// shipped default would be both a secret in the repository and a working one.
    /// </summary>
    [Fact]
    public void RabbitMqOptions_CarryNoDefaultCredentials()
    {
        RabbitMqOptions defaults = new();

        defaults.UserName.Should().BeEmpty();
        defaults.Password.Should().BeEmpty();
    }

    [Fact]
    public async Task AddMessaging_WithoutConsumers_StillRegistersTheBus()
    {
        await using ServiceProvider provider = Configured();

        provider.GetRequiredService<IBus>().Should().NotBeNull();
    }

    private static ServiceProvider Configured()
    {
        ServiceCollection services = [];
        services.AddMessaging(Configuration());

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:HostName"] = "127.0.0.1",
                ["RabbitMq:Port"] = "5672",
                ["RabbitMq:VirtualHost"] = "/",
                ["RabbitMq:UserName"] = "configured-user",
                ["RabbitMq:Password"] = "configured-password",
            })
            .Build();
}
