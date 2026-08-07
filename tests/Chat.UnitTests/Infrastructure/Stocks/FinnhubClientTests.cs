using System.Net;
using System.Text;
using Chat.Application.Abstractions.Stocks;
using Chat.Application.Contracts.Messaging;
using Chat.Domain.Common;
using Chat.Domain.StockCommands;
using Chat.Infrastructure.Stocks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chat.UnitTests.Infrastructure.Stocks;

/// <summary>
/// Covers the HTTP half of the Finnhub adapter with a stubbed transport, the same way
/// <see cref="StooqClientTests"/> covers Stooq: no socket is opened and the configured host is
/// <c>.invalid</c>, so a regression that bypassed the stub would fail rather than quietly call the real
/// service — and would not spend the free key's rate budget.
/// </summary>
/// <remarks>
/// This is the provider the solution ships as the default, so its branches are the ones a reviewer's
/// clone actually executes. The keyless short-circuit in particular cannot be reached from the
/// integration suite, which always supplies a key.
/// </remarks>
public sealed class FinnhubClientTests
{
    private const string QuoteBody =
        """{"c":311.51,"d":-0.15,"dp":-0.0573,"h":313.31,"l":310.68,"o":311.07,"pc":311.89,"t":1582641000}""";

    private const string UnknownSymbolBody = """{"c":0,"d":null,"dp":null,"h":0,"l":0,"o":0,"pc":0,"t":0}""";

    private const string ValidKey = "a-key";

    /// <summary>The options every test starts from; only the key ever varies.</summary>
    private static FinnhubOptions Settings(string apiKey = ValidKey) =>
        new()
        {
            BaseAddress = new Uri("https://quotes.invalid/"),
            QuotePath = "api/v1/quote?symbol={0}&token={1}",
            TimeoutSeconds = 10,
            ApiKey = apiKey,
        };

    /// <summary>
    /// The gap a fresh clone starts in. Answering without calling Finnhub matters twice: an unkeyed
    /// request is a guaranteed 401, and the operator needs the log line naming the key rather than a
    /// transport error that reads like an outage.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetQuoteAsync_WithoutAnApiKey_ReturnsLookupFailedWithoutCallingTheService(string apiKey)
    {
        StubHandler handler = new(Responds(HttpStatusCode.OK, QuoteBody));
        FinnhubClient client = Create(handler, apiKey);

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
        lookup.Price.Should().BeNull();
        handler.Calls.Should().Be(0, "an unkeyed request can only be rejected, so it is not worth sending");
    }

    [Fact]
    public async Task GetQuoteAsync_ValidQuote_ReturnsTheCurrentPrice()
    {
        FinnhubClient client = Create(new StubHandler(Responds(HttpStatusCode.OK, QuoteBody)));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.Quoted);
        lookup.Price.Should().Be(311.51m);
    }

    /// <summary>Every number zero is how Finnhub answers a ticker it does not carry — not an error status.</summary>
    [Fact]
    public async Task GetQuoteAsync_UnknownSymbol_ReturnsSymbolNotFound()
    {
        FinnhubClient client = Create(new StubHandler(Responds(HttpStatusCode.OK, UnknownSymbolBody)));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("zzzz.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.SymbolNotFound);
        lookup.Price.Should().BeNull();
    }

    /// <summary>
    /// The URL is composed from <see cref="FinnhubOptions"/>, the validated code and the configured key,
    /// all escaped. The stubbed <c>HttpClient</c> has no base address, so a URL built anywhere else would
    /// not even be absolute.
    /// </summary>
    [Fact]
    public async Task GetQuoteAsync_Always_BuildsTheUrlFromTheOptionsAndTheValidatedCode()
    {
        StubHandler handler = new(Responds(HttpStatusCode.OK, QuoteBody));
        FinnhubClient client = Create(handler);

        await client.GetQuoteAsync(Code("AAPL.US"), CancellationToken.None);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsoluteUri.Should()
            .Be("https://quotes.invalid/api/v1/quote?symbol=AAPL&token=a-key");
    }

    /// <summary>
    /// A key is operator-supplied text, not a validated value object, so it is the one part of the query
    /// that could carry a delimiter. Escaping keeps it a single parameter value instead of letting it add
    /// or overwrite one.
    /// </summary>
    [Fact]
    public async Task GetQuoteAsync_ApiKeyCarryingQueryDelimiters_EscapesItIntoASingleValue()
    {
        StubHandler handler = new(Responds(HttpStatusCode.OK, QuoteBody));
        FinnhubClient client = Create(handler, "k&symbol=zzzz");

        await client.GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        handler.LastRequest!.RequestUri!.AbsoluteUri.Should()
            .Be("https://quotes.invalid/api/v1/quote?symbol=AAPL&token=k%26symbol%3Dzzzz");
    }

    /// <summary>
    /// A rejected key is an operator's problem rather than a transient one, so the client logs it
    /// distinctly — but the room still gets an answer, which is what this asserts.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetQuoteAsync_RejectedApiKey_ReturnsLookupFailed(HttpStatusCode status)
    {
        FinnhubClient client = Create(new StubHandler(Responds(status, string.Empty)));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
        lookup.Price.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetQuoteAsync_NonSuccessStatus_ReturnsLookupFailed(HttpStatusCode status)
    {
        FinnhubClient client = Create(new StubHandler(Responds(status, QuoteBody)));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
        lookup.Price.Should().BeNull();
    }

    /// <summary>A proxy or a captive portal can answer 200 with something that is not a quote at all.</summary>
    [Theory]
    [InlineData("<html><body>Sign in to continue</body></html>")]
    [InlineData("")]
    [InlineData("{\"error\":\"You don't have access to this resource.\"}")]
    public async Task GetQuoteAsync_BodyCarryingNoPrice_ReturnsLookupFailed(string body)
    {
        FinnhubClient client = Create(new StubHandler(Responds(HttpStatusCode.OK, body)));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    [Fact]
    public async Task GetQuoteAsync_TransportFailure_ReturnsLookupFailed()
    {
        FinnhubClient client = Create(new StubHandler((_, _) =>
            throw new HttpRequestException("No such host is known.")));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    /// <summary>
    /// A client-side timeout surfaces as a <see cref="TaskCanceledException"/> wrapping a
    /// <see cref="TimeoutException"/> while the caller's token stays unsignalled — the case that must be
    /// answered politely, not rethrown.
    /// </summary>
    [Fact]
    public async Task GetQuoteAsync_Timeout_ReturnsLookupFailed()
    {
        FinnhubClient client = Create(new StubHandler((_, _) =>
            throw new TaskCanceledException("The request was canceled due to a timeout.", new TimeoutException())));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    /// <summary>
    /// Anything else the transport or the resilience pipeline can throw — an open circuit, a disposed
    /// handler, an I/O fault — is still an answer the bot can speak, because the port must not throw.
    /// </summary>
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(NotSupportedException))]
    public async Task GetQuoteAsync_UnexpectedTransportException_ReturnsLookupFailed(Type exceptionType)
    {
        FinnhubClient client = Create(new StubHandler((_, _) =>
            throw (Exception)Activator.CreateInstance(exceptionType)!));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    /// <summary>
    /// The caller giving up is not a lookup failure: swallowing it would post "could not look that up"
    /// into a room whose request was already abandoned, and would hide shutdown from the consumer.
    /// </summary>
    [Fact]
    public async Task GetQuoteAsync_CallerCancels_PropagatesTheCancellation()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        FinnhubClient client = Create(new StubHandler(Responds(HttpStatusCode.OK, QuoteBody)));

        Func<Task> lookup = () => client.GetQuoteAsync(Code("aapl.us"), cancellation.Token);

        await lookup.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetQuoteAsync_Always_LinksTheCallersTokenToTheTransportCall()
    {
        using CancellationTokenSource cancellation = new();
        bool transportSawTheCancellation = false;

        StubHandler handler = new((_, token) =>
        {
            cancellation.Cancel();
            transportSawTheCancellation = token.IsCancellationRequested;
            token.ThrowIfCancellationRequested();

            return Task.FromResult(Response(HttpStatusCode.OK, QuoteBody));
        });

        FinnhubClient client = Create(handler);

        Func<Task> lookup = () => client.GetQuoteAsync(Code("aapl.us"), cancellation.Token);

        await lookup.Should().ThrowAsync<OperationCanceledException>();
        transportSawTheCancellation.Should().BeTrue();
    }

    [Fact]
    public async Task GetQuoteAsync_NullStockCode_Throws()
    {
        FinnhubClient client = Create(new StubHandler(Responds(HttpStatusCode.OK, QuoteBody)));

        Func<Task> lookup = () => client.GetQuoteAsync(null!, CancellationToken.None);

        await lookup.Should().ThrowAsync<ArgumentNullException>();
    }

    private static FinnhubClient Create(StubHandler handler, string apiKey = ValidKey)
    {
        HttpClient httpClient = new(handler, disposeHandler: true);

        return new FinnhubClient(
            httpClient,
            Options.Create(Settings(apiKey)),
            NullLogger<FinnhubClient>.Instance);
    }

    private static StockCode Code(string value)
    {
        Result<StockCode> code = StockCode.Create(value);
        code.IsSuccess.Should().BeTrue();

        return code.Value;
    }

    /// <summary>
    /// Answers with a fixed response, honouring cancellation the way a real transport does —
    /// <c>HttpClient</c> itself does not check the token before handing the request to its handler.
    /// </summary>
    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Responds(
        HttpStatusCode status,
        string body) =>
        (_, token) =>
        {
            token.ThrowIfCancellationRequested();

            return Task.FromResult(Response(status, body));
        };

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Records the outbound requests and answers with whatever the test dictates.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        /// <summary>How many requests reached the transport — 0 proves a short-circuit.</summary>
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            Calls++;

            return respond(request, cancellationToken);
        }
    }
}
