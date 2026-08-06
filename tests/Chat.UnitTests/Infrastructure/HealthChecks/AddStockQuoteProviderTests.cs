using System.Net;
using Chat.Infrastructure;
using Chat.Infrastructure.HealthChecks;
using Chat.Infrastructure.Stocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Chat.UnitTests.Infrastructure.HealthChecks;

/// <summary>
/// The bot's quote-provider probe must follow <c>Stocks:Provider</c>. A probe of a service the process
/// never calls is worse than no probe: it reports green for the wrong dependency, and stays green through
/// an outage of the one that matters.
/// </summary>
public sealed class AddStockQuoteProviderTests
{
    [Fact]
    public void AddStockQuoteProvider_WhenTheProviderIsAbsent_ProbesFinnhub()
    {
        Registration(provider: null).Should().BeOfType<FinnhubHealthCheck>(
            "Finnhub is the default, so a host that configures nothing must probe Finnhub");
    }

    [Theory]
    [InlineData("Finnhub")]
    [InlineData("finnhub")]
    public void AddStockQuoteProvider_WhenTheProviderIsFinnhub_ProbesFinnhub(string provider)
    {
        Registration(provider).Should().BeOfType<FinnhubHealthCheck>();
    }

    [Theory]
    [InlineData("Stooq")]
    [InlineData("stooq")]
    public void AddStockQuoteProvider_WhenTheProviderIsStooq_ProbesStooq(string provider)
    {
        Registration(provider).Should().BeOfType<StooqHealthCheck>();
    }

    /// <summary>
    /// The payload's shape must not change with configuration: one name, whichever provider answers, so a
    /// monitor watching <c>/health</c> does not have to know how the bot is configured.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Finnhub")]
    [InlineData("Stooq")]
    public void AddStockQuoteProvider_WhicheverProvider_RegistersOneExternalCheckUnderTheSameName(string? provider)
    {
        HealthCheckRegistration registration = Registrations(provider).Should().ContainSingle().Subject;

        registration.Name.Should().Be(HealthCheckNames.StockQuoteProvider);
        registration.Name.Should().NotContain("stooq", "the name is the role, never the vendor");
        registration.FailureStatus.Should().Be(
            HealthStatus.Degraded,
            "a third-party outage degrades the bot, it does not break it");
        registration.Tags.Should().BeEquivalentTo(
            [HealthCheckNames.ExternalTag],
            "a third-party outage must never mark the process unready");
    }

    /// <summary>A typo must fail at startup, not quietly probe the other provider.</summary>
    [Fact]
    public void AddStockQuoteProvider_WhenTheProviderIsUnknown_FailsFast()
    {
        ServiceCollection services = [];

        services.Invoking(collection => collection.AddHealthChecks().AddStockQuoteProvider(Configuration("Yahoo")))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Yahoo*")
            .WithMessage("*Stooq*")
            .WithMessage("*Finnhub*");
    }

    [Fact]
    public void AddStockQuoteProvider_NullConfiguration_Throws()
    {
        ServiceCollection services = [];

        services.Invoking(collection => collection.AddHealthChecks().AddStockQuoteProvider(null!))
            .Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// The gap most likely to be left on a fresh clone, and the one nothing else reports: without a key
    /// every lookup answers a friendly failure, which looks like a broken bot rather than missing setup.
    /// </summary>
    [Fact]
    public async Task FinnhubHealthCheck_WithoutAnApiKey_IsDegradedAndSaysWhichKeyIsMissing()
    {
        HealthCheckResult result = await CheckFinnhubAsync(apiKey: string.Empty, HttpStatusCode.OK);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("Finnhub:ApiKey").And.Contain("user-secrets");
    }

    /// <summary>The description is where a reader learns which provider answered, so it names it.</summary>
    [Fact]
    public async Task FinnhubHealthCheck_WhenTheServiceAnswers_IsHealthyAndNamesTheProvider()
    {
        HealthCheckResult result = await CheckFinnhubAsync("a-key", HttpStatusCode.OK);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Finnhub responded 200.");
    }

    [Fact]
    public async Task FinnhubHealthCheck_WhenTheServiceFails_IsDegradedRatherThanUnhealthy()
    {
        HealthCheckResult result = await CheckFinnhubAsync("a-key", HttpStatusCode.ServiceUnavailable);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("Finnhub responded 503.");
    }

    [Fact]
    public async Task FinnhubHealthCheck_WhenTheServiceIsUnreachable_IsDegradedAndCarriesTheCause()
    {
        using HttpClient client = new(new ThrowingHandler());
        FinnhubHealthCheck check = new(client, Options.Create(new FinnhubOptions { ApiKey = "a-key" }));

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("Finnhub is unreachable.");
        result.Exception.Should().BeOfType<HttpRequestException>();
    }

    private static async Task<HealthCheckResult> CheckFinnhubAsync(string apiKey, HttpStatusCode status)
    {
        using HttpClient client = new(new StubHandler(status));
        FinnhubHealthCheck check = new(client, Options.Create(new FinnhubOptions { ApiKey = apiKey }));

        return await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
    }

    /// <summary>The health check instance one <c>Stocks:Provider</c> value produces.</summary>
    private static IHealthCheck Registration(string? provider)
    {
        ServiceCollection services = [];
        services.AddHealthChecks().AddStockQuoteProvider(Configuration(provider));

        using ServiceProvider built = services.BuildServiceProvider(validateScopes: true);

        return Registrations(provider).Single().Factory(built);
    }

    private static IReadOnlyList<HealthCheckRegistration> Registrations(string? provider)
    {
        ServiceCollection services = [];
        services.AddHealthChecks().AddStockQuoteProvider(Configuration(provider));

        using ServiceProvider built = services.BuildServiceProvider(validateScopes: true);

        return [.. built.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }

    private static IConfiguration Configuration(string? provider)
    {
        Dictionary<string, string?> settings = [];

        if (provider is not null)
        {
            settings[DependencyInjection.StockQuoteProviderKey] = provider;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("no route to host");
    }
}
