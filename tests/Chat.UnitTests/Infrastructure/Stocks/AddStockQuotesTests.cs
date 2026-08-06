using System.Net;
using Chat.Application.Abstractions.Stocks;
using Chat.Application.Contracts.Messaging;
using Chat.Domain.Common;
using Chat.Domain.StockCommands;
using Chat.Infrastructure;
using Chat.Infrastructure.Stocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Chat.UnitTests.Infrastructure.Stocks;

/// <summary>
/// Covers what Chat.Bot gets from <c>AddStockQuotes</c>. The primary handler is replaced with a stub, so
/// the resilience behaviour is exercised without a socket.
/// </summary>
public sealed class AddStockQuotesTests
{
    /// <summary>
    /// How <c>AddStandardResilienceHandler</c> names its own options: the client name plus
    /// <c>-standard</c>. Measured, because the suffix is not part of the package's documented surface.
    /// </summary>
    private const string ResilienceOptionsName = $"{StooqClient.HttpClientName}-standard";

    [Fact]
    public async Task AddStockQuotes_ResolvesTheQuoteProviderPort()
    {
        await using ServiceProvider provider = Configured();

        provider.GetRequiredService<IStockQuoteProvider>().Should().BeOfType<StooqClient>();
    }

    [Fact]
    public async Task AddStockQuotes_BindsTheStooqSettingsFromConfiguration()
    {
        await using ServiceProvider provider = Configured();

        StooqOptions options = provider.GetRequiredService<IOptions<StooqOptions>>().Value;

        options.BaseAddress.Should().Be(new Uri("https://quotes.invalid/"));
        options.QuotePath.Should().Be("q/l/?s={0}&f=configured&e=csv");
        options.TimeoutSeconds.Should().Be(7);
    }

    /// <summary>
    /// The buffered response is bounded: an endpoint that streamed forever must not be able to grow the
    /// bot's memory without limit.
    /// <para>
    /// The client timeout is deliberately infinite. Measured: <c>AddStandardResilienceHandler</c> appends
    /// its own client action that sets it, so the pipeline owns the budget — asserted here so the two
    /// facts stay recorded together rather than looking like an oversight.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AddStockQuotes_ConfiguresTheTypedClientFromOptions()
    {
        await using ServiceProvider provider = Configured();

        using HttpClient client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(StooqClient.HttpClientName);

        client.MaxResponseContentBufferSize.Should().Be(StooqClient.MaxResponseBytes);
        client.Timeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// The configured timeout is the pipeline's total budget, split into
    /// <see cref="StooqClient.MaxAttemptsPerLookup"/> attempts, so one slow attempt cannot spend the
    /// budget the retries need.
    /// </summary>
    [Fact]
    public async Task AddStockQuotes_AppliesTheConfiguredTimeoutAsTheResilienceBudget()
    {
        await using ServiceProvider provider = Configured();

        HttpStandardResilienceOptions resilience = provider
            .GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get(ResilienceOptionsName);

        resilience.TotalRequestTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(7));
        resilience.AttemptTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(7) / StooqClient.MaxAttemptsPerLookup);
        resilience.Retry.MaxRetryAttempts.Should().Be(StooqClient.MaxAttemptsPerLookup - 1);
        resilience.CircuitBreaker.SamplingDuration.Should()
            .BeGreaterThanOrEqualTo(resilience.AttemptTimeout.Timeout * 2, "the standard handler validates it");
    }

    /// <summary>
    /// Proves the standard resilience handler is actually in the chain: a 500 is retried up to
    /// <see cref="StooqClient.MaxAttemptsPerLookup"/> attempts and the caller still gets an answer rather
    /// than an exception.
    /// </summary>
    [Fact]
    public async Task AddStockQuotes_TransientFailure_IsRetriedWithinOneLookup()
    {
        int attempts = 0;
        await using ServiceProvider provider = Configured(_ =>
        {
            attempts++;

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        StockQuoteLookup lookup = await provider.GetRequiredService<IStockQuoteProvider>()
            .GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        attempts.Should().Be(StooqClient.MaxAttemptsPerLookup);
        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    /// <summary>A successful lookup is not retried — one request per quote is the whole resource budget.</summary>
    [Fact]
    public async Task AddStockQuotes_SuccessfulLookup_IssuesExactlyOneRequest()
    {
        int attempts = 0;
        await using ServiceProvider provider = Configured(_ =>
        {
            attempts++;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Symbol,Close\nAAPL.US,206.55"),
            };
        });

        StockQuoteLookup lookup = await provider.GetRequiredService<IStockQuoteProvider>()
            .GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        attempts.Should().Be(1);
        lookup.Price.Should().Be(206.55m);
    }

    /// <summary>
    /// Pins the endpoint the challenge specifies. The template is the only place a stock code is ever
    /// interpolated, so a change here changes the outbound contract.
    /// </summary>
    [Fact]
    public void StooqOptions_Defaults_MatchTheChallengeEndpoint()
    {
        StooqOptions defaults = new();

        defaults.BaseAddress.Should().Be(new Uri("https://stooq.com/"));
        defaults.QuotePath.Should().Be("q/d/l/?s={0}&i=d");
        defaults.TimeoutSeconds.Should().Be(10);
    }

    [Fact]
    public void AddStockQuotes_NullConfiguration_Throws()
    {
        ServiceCollection services = [];

        services.Invoking(collection => collection.AddStockQuotes(null!))
            .Should().Throw<ArgumentNullException>();
    }

    private static ServiceProvider Configured(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        ServiceCollection services = [];
        services.AddStockQuotes(Configuration());

        if (respond is not null)
        {
            services.AddHttpClient(StooqClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(respond));
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stooq:BaseAddress"] = "https://quotes.invalid/",
                ["Stooq:QuotePath"] = "q/l/?s={0}&f=configured&e=csv",
                ["Stooq:TimeoutSeconds"] = "7",
            })
            .Build();

    private static StockCode Code(string value)
    {
        Result<StockCode> code = StockCode.Create(value);
        code.IsSuccess.Should().BeTrue();

        return code.Value;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
