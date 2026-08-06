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
/// <c>MessagingConstants</c> names, which dispatches the real handler, which calls the real
/// <c>StooqClient</c> and publishes a real <see cref="StockQuoteResolved"/>.
/// </summary>
/// <remarks>
/// <b>Only the network is substituted.</b> Everything between the broker and the HTTP boundary is the
/// shipped code — the consumer, the MediatR pipeline, the typed client with its resilience handler, the CSV
/// parser and the answer wording. Stooq itself is a stubbed <see cref="HttpMessageHandler"/>, because these
/// tests exist to pin how a <i>given</i> response becomes an answer in the chat room; calling the live
/// service would make them depend on a third party that currently cannot be reached at all.
/// <para>
/// No database and no container: the bot has neither, which is the point of it. So these are plain
/// <c>[Fact]</c>s rather than <see cref="DockerFactAttribute"/>s and they run everywhere, and the class
/// deliberately joins no collection so the SQL Server fixture is never started for them.
/// </para>
/// </remarks>
public sealed class StockQuoteResolutionTests
{
    /// <summary>The real body Stooq returns for <c>/q/d/l/?s=aavvf.us&amp;i=d</c>, trimmed to four sessions.</summary>
    private const string DailyHistory =
        "Date,Open,High,Low,Close,Volume\n"
        + "2026-03-09,8,8.09,7.77,7.84,134600\n"
        + "2026-07-31,7.85,8.11,7.82,7.86,113000\n"
        + "2026-08-04,7.78,7.8,7.68,7.68,136000\n"
        + "2026-08-05,7.51,7.71,7.5,7.69,1250\n";

    /// <summary>What Stooq answers for a ticker it will not serve — with HTTP 200, not a 404.</summary>
    private const string AccessDenied = "Access denied";

    [Fact]
    public async Task StockQuoteRequested_WhenStooqReturnsADailyHistory_PublishesTheNewestSessionsClose()
    {
        await using Harness harness = await Harness.StartAsync(HttpStatusCode.OK, DailyHistory);

        StockQuoteResolved answer = await harness.ResolveAsync("aavvf.us");

        answer.Outcome.Should().Be(StockQuoteOutcome.Quoted);
        answer.Price.Should().Be(7.69m, "the newest session closed at 7.69");
        answer.Message.Should().Be(
            "AAVVF.US quote is $7.69 per share",
            "this is the wording the challenge specifies, with the ticker in upper case");
    }

    /// <summary>
    /// Stooq refuses any client outside a verified browser session with "Access denied" and HTTP 200 —
    /// measured for a valid ticker as well as a misspelled one, so it cannot be read as an unknown symbol.
    /// </summary>
    [Fact]
    public async Task StockQuoteRequested_WhenStooqRefusesTheRequest_PublishesLookupFailed()
    {
        await using Harness harness = await Harness.StartAsync(HttpStatusCode.OK, AccessDenied);

        StockQuoteResolved answer = await harness.ResolveAsync("aavvf.us");

        answer.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
        answer.Price.Should().BeNull();
        answer.Message.Should().Be(
            "I could not reach the quote service, so I have no price for AAVVF.US right now.");
    }

    /// <summary>
    /// The genuine unknown-symbol signal is <c>N/D</c> inside a real CSV row, which is what the
    /// single-quote path returns. That one does mean the ticker does not exist.
    /// </summary>
    [Fact]
    public async Task StockQuoteRequested_WhenTheCsvSaysNotAvailable_PublishesSymbolNotFound()
    {
        const string notAvailable =
            "Symbol,Date,Time,Open,High,Low,Close,Volume\nZZZZ.US,N/D,N/D,N/D,N/D,N/D,N/D,N/D\n";

        await using Harness harness = await Harness.StartAsync(HttpStatusCode.OK, notAvailable);

        StockQuoteResolved answer = await harness.ResolveAsync("zzzz.us");

        answer.Outcome.Should().Be(StockQuoteOutcome.SymbolNotFound);
        answer.Message.Should().Be("Sorry, I could not find a quote for ZZZZ.US.");
    }

    /// <summary>
    /// A real service failure, by contrast, is what the outage banner exists for — so it must arrive as
    /// <see cref="StockQuoteOutcome.LookupFailed"/> and not as a symbol that does not exist.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task StockQuoteRequested_WhenStooqFails_PublishesLookupFailed(HttpStatusCode status)
    {
        await using Harness harness = await Harness.StartAsync(status, "<html>error</html>");

        StockQuoteResolved answer = await harness.ResolveAsync("aavvf.us");

        answer.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
        answer.Price.Should().BeNull();
        answer.Message.Should().Be(
            "I could not reach the quote service, so I have no price for AAVVF.US right now.");
    }

    /// <summary>
    /// The browser-verification page Stooq currently serves to any HTTP client: HTTP 200, but HTML. It says
    /// nothing about the ticker, so it is a failed lookup rather than an unknown symbol.
    /// </summary>
    [Fact]
    public async Task StockQuoteRequested_WhenStooqServesItsBrowserCheck_PublishesLookupFailed()
    {
        const string challenge =
            "<!DOCTYPE html><html><body><noscript>This site requires JavaScript to verify your browser."
            + "</noscript><script>/* proof of work */</script></body></html>";

        await using Harness harness = await Harness.StartAsync(HttpStatusCode.OK, challenge);

        StockQuoteResolved answer = await harness.ResolveAsync("aavvf.us");

        answer.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    /// <summary>
    /// The bot's composition root, minus the network: <c>AddApplication&lt;IBotFeature&gt;</c>, the clock,
    /// the bus with the real request consumer on the real endpoint name, and <c>AddStockQuotes</c> with its
    /// outermost HTTP handler replaced by a stub.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly ITestHarness bus;

        private Harness(ServiceProvider provider, ITestHarness bus)
        {
            this.provider = provider;
            this.bus = bus;
        }

        public static async Task<Harness> StartAsync(HttpStatusCode status, string body)
        {
            IConfiguration configuration = new ConfigurationBuilder().Build();
            ServiceCollection services = [];

            // Chat.Bot's own composition, in its own order. AddMessaging is what registers the responder
            // the handler publishes through and the consumer on MessagingConstants' endpoint name.
            services.AddApplication<IBotFeature>();
            services.AddSystemClock();
            services.AddMessaging(
                configuration,
                configurator => configurator.AddStockQuoteRequestConsumer<StockQuoteRequestConsumer>());
            services.AddStockQuotes(configuration);

            // Replace only the transport, exactly as ChatApplicationFactory does for the web host: the
            // consumer registrations, the publisher adapters and the endpoint names all survive, and
            // nothing tries to reach RabbitMQ.
            services.AddMassTransitTestHarness(configurator =>
                configurator.SetTestTimeouts(ChatApplicationFactory.BusTimeout, ChatApplicationFactory.BusTimeout));

            // The typed client, its options, the resilience pipeline and the parser stay as registered;
            // only the outermost handler is a stub, so no test can reach the live service.
            services.AddHttpClient(StooqClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(status, body));

            ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            ITestHarness bus = provider.GetRequiredService<ITestHarness>();
            await bus.Start();

            return new Harness(provider, bus);
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

            IPublishedMessage<StockQuoteResolved> published = bus.Published
                .Select<StockQuoteResolved>()
                .First(message => message.Context.Message.RequestId == request.RequestId);

            return published.Context.Message;
        }

        public async ValueTask DisposeAsync()
        {
            await bus.Stop().ConfigureAwait(false);
            await provider.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Answers every request with one canned response. Never reaches the network.</summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
