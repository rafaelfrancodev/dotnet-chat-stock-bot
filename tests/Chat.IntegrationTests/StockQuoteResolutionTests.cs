using System.Net;
using Chat.Application;
using Chat.Application.Abstractions.Hosting;
using Chat.Application.Contracts.Messaging;
using Chat.Bot;
using Chat.Infrastructure;
using Chat.Infrastructure.Messaging;
using Chat.Infrastructure.Stocks;
using Chat.IntegrationTests.Infrastructure;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.IntegrationTests;

/// <summary>
/// The bot half of the stock flow, wired the way <c>Chat.Bot</c> wires it: a published
/// <see cref="StockQuoteRequested"/> reaches the real <c>StockQuoteRequestConsumer</c> on the endpoint
/// <c>MessagingConstants</c> names, which dispatches the real handler, which calls the real quote client
/// and publishes a real <see cref="StockQuoteResolved"/>.
/// </summary>
/// <remarks>
/// <b>Configured for Finnhub</b>, because that is the provider the application actually runs on: Stooq's
/// CSV endpoint is no longer readable from a server. The Stooq adapter keeps its own unit tests, so both
/// providers stay covered — this exercises the live path end to end.
/// <para>
/// <b>Only the network is substituted.</b> Everything between the broker and the HTTP boundary is the
/// shipped code — the consumer, the MediatR pipeline, the typed client with its resilience handler, the
/// JSON parser, the symbol translation and the answer wording. The response bodies are the real ones,
/// captured from the live API, so this pins how a given response becomes a line in the chat room without
/// depending on a third party at test time.
/// </para>
/// <para>
/// No database and no container: the bot has neither, which is the point of it. So these are plain
/// <c>[Fact]</c>s rather than <see cref="DockerFactAttribute"/>s and they run everywhere.
/// </para>
/// </remarks>
public sealed class StockQuoteResolutionTests
{
    /// <summary>A real Finnhub quote body, captured from the live API on 2026-08-06 for AAPL.</summary>
    private const string QuoteBody =
        """{"c":311.55,"d":0.55,"dp":0.1768,"h":316.2894,"l":309.23,"o":313.73,"pc":311,"t":1786038273}""";

    /// <summary>Finnhub's unknown-symbol answer: HTTP 200 with every number zero.</summary>
    private const string UnknownSymbolBody =
        """{"c":0,"d":null,"dp":null,"h":0,"l":0,"o":0,"pc":0,"t":0}""";

    /// <summary>The transport is stubbed, so the key never has to be real — only present.</summary>
    private const string ApiKey = "integration-test-key";

    [Fact]
    public async Task StockQuoteRequested_WhenTheProviderAnswers_PublishesTheChallengesWording()
    {
        await using Harness harness = await Harness.StartAsync(HttpStatusCode.OK, QuoteBody);

        StockQuoteResolved answer = await harness.ResolveAsync("aapl.us");

        answer.Outcome.Should().Be(StockQuoteOutcome.Quoted);
        answer.Price.Should().Be(311.55m, "c is the current price");
        answer.Message.Should().Be(
            "AAPL.US quote is $311.55 per share",
            "this is the wording the challenge specifies, with the ticker upper-cased");
    }

    /// <summary>
    /// The ticker reaches Finnhub in the form it expects: the chat's <c>aapl.us</c> becomes <c>AAPL</c>,
    /// and the API key travels as a query parameter. Asserted on the request the client actually sent.
    /// </summary>
    [Fact]
    public async Task StockQuoteRequested_Always_CallsTheProviderWithTheTranslatedSymbol()
    {
        await using Harness harness = await Harness.StartAsync(HttpStatusCode.OK, QuoteBody);

        await harness.ResolveAsync("aapl.us");

        harness.LastRequestUri.Should().NotBeNull();
        harness.LastRequestUri!.AbsoluteUri.Should().Contain("symbol=AAPL").And.Contain($"token={ApiKey}");
    }

    /// <summary>
    /// The case a participant hits by mistyping. It must be a friendly answer with no outage banner: the
    /// service is working, the symbol simply does not exist.
    /// </summary>
    [Fact]
    public async Task StockQuoteRequested_WhenTheSymbolIsUnknown_PublishesSymbolNotFound()
    {
        await using Harness harness = await Harness.StartAsync(HttpStatusCode.OK, UnknownSymbolBody);

        StockQuoteResolved answer = await harness.ResolveAsync("zzzznotreal.us");

        answer.Outcome.Should().Be(StockQuoteOutcome.SymbolNotFound);
        answer.Price.Should().BeNull();
        answer.Message.Should().Be("Sorry, I could not find a quote for ZZZZNOTREAL.US.");
    }

    /// <summary>
    /// A real service failure is what the outage banner exists for, so it must arrive as
    /// <see cref="StockQuoteOutcome.LookupFailed"/> and never as a symbol that does not exist. 401 is in
    /// the list because a rejected API key must not be reported to a participant as a bad ticker.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task StockQuoteRequested_WhenTheProviderFails_PublishesLookupFailed(HttpStatusCode status)
    {
        await using Harness harness = await Harness.StartAsync(status, """{"error":"nope"}""");

        StockQuoteResolved answer = await harness.ResolveAsync("aapl.us");

        answer.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
        answer.Price.Should().BeNull();
        answer.Message.Should().Be(
            "I could not reach the quote service, so I have no price for AAPL.US right now.");
    }

    /// <summary>An unreadable body is the service's problem, not a verdict on the ticker.</summary>
    [Fact]
    public async Task StockQuoteRequested_WhenTheBodyIsNotJson_PublishesLookupFailed()
    {
        await using Harness harness = await Harness.StartAsync(HttpStatusCode.OK, "<html>gateway error</html>");

        StockQuoteResolved answer = await harness.ResolveAsync("aapl.us");

        answer.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    /// <summary>
    /// The bot's composition root, minus the network: <c>AddApplication&lt;IBotFeature&gt;</c>, the clock,
    /// the bus with the real request consumer on the real endpoint name, and <c>AddStockQuotes</c>
    /// configured for Finnhub with its outermost HTTP handler replaced by a stub.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly ITestHarness bus;
        private readonly StubHandler handler;

        private Harness(ServiceProvider provider, ITestHarness bus, StubHandler handler)
        {
            this.provider = provider;
            this.bus = bus;
            this.handler = handler;
        }

        /// <summary>The URL the quote client actually requested, for asserting symbol translation.</summary>
        public Uri? LastRequestUri => handler.LastRequestUri;

        public static async Task<Harness> StartAsync(HttpStatusCode status, string body)
        {
            // Selects the provider exactly as a deployment does, through configuration.
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [Chat.Infrastructure.DependencyInjection.StockQuoteProviderKey] =
                        Chat.Infrastructure.DependencyInjection.FinnhubProvider,
                    [$"{FinnhubOptions.SectionName}:{nameof(FinnhubOptions.ApiKey)}"] = ApiKey,
                })
                .Build();

            ServiceCollection services = [];

            services.AddApplication<IBotFeature>();
            services.AddSystemClock();
            services.AddMessaging(
                configuration,
                configurator => configurator.AddStockQuoteRequestConsumer<StockQuoteRequestConsumer>());
            services.AddStockQuotes(configuration);

            services.AddMassTransitTestHarness(configurator =>
                configurator.SetTestTimeouts(ChatApplicationFactory.BusTimeout, ChatApplicationFactory.BusTimeout));

            StubHandler handler = new(status, body);

            services.AddHttpClient(FinnhubClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler);

            ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            ITestHarness bus = provider.GetRequiredService<ITestHarness>();
            await bus.Start();

            return new Harness(provider, bus, handler);
        }

        /// <summary>Publishes a request as Chat.Web would, and returns the answer the bot published.</summary>
        public async Task<StockQuoteResolved> ResolveAsync(string stockCode)
        {
            StockQuoteRequested request = new(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                stockCode,
                "user-1",
                "Alice Anderson",
                DateTimeOffset.UtcNow);

            await bus.Bus.Publish(request);

            (await bus.Published.Any<StockQuoteResolved>()).Should().BeTrue(
                "the bot must answer every request it accepts");

            return bus.Published
                .Select<StockQuoteResolved>()
                .First(message => message.Context.Message.RequestId == request.RequestId)
                .Context.Message;
        }

        public async ValueTask DisposeAsync()
        {
            await bus.Stop().ConfigureAwait(false);
            await provider.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Answers every request with one canned response, and records what was asked.</summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
