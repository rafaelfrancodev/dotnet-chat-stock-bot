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
/// Covers the HTTP half of the Stooq adapter with a stubbed transport: no socket is opened and the
/// configured host is <c>.invalid</c>, so a regression that bypassed the stub would fail rather than
/// quietly call the real service.
/// </summary>
public sealed class StooqClientTests
{
    private const string Header = "Symbol,Date,Time,Open,High,Low,Close,Volume";
    private const string QuoteBody = $"{Header}\r\nAAPL.US,2026-08-04,21:00:00,205.1,207.2,204.4,206.55,42193021\r\n";
    private const string NotFoundBody = $"{Header}\r\nZZZZ.US,N/D,N/D,N/D,N/D,N/D,N/D,N/D\r\n";

    private static readonly StooqOptions Settings = new()
    {
        BaseAddress = new Uri("https://quotes.invalid/"),
        QuotePath = "q/l/?s={0}&f=sd2t2ohlcv&h&e=csv",
        TimeoutSeconds = 10,
    };

    [Fact]
    public async Task GetQuoteAsync_ValidCsv_ReturnsTheClosingPrice()
    {
        StooqClient client = Create(new StubHandler(Responds(HttpStatusCode.OK, QuoteBody)));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.Quoted);
        lookup.Price.Should().Be(206.55m);
    }

    /// <summary>
    /// The URL is composed from <see cref="StooqOptions"/> and the validated code only. The stubbed
    /// <c>HttpClient</c> has no base address, so a URL built anywhere else would not even be absolute.
    /// </summary>
    [Fact]
    public async Task GetQuoteAsync_Always_BuildsTheUrlFromTheOptionsAndTheValidatedCode()
    {
        StubHandler handler = new(Responds(HttpStatusCode.OK, QuoteBody));
        StooqClient client = Create(handler);

        await client.GetQuoteAsync(Code("AAPL.US"), CancellationToken.None);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsoluteUri.Should()
            .Be("https://quotes.invalid/q/l/?s=aapl.us&f=sd2t2ohlcv&h&e=csv");
    }

    [Fact]
    public async Task GetQuoteAsync_UnknownSymbol_ReturnsSymbolNotFound()
    {
        StooqClient client = Create(new StubHandler(Responds(HttpStatusCode.OK, NotFoundBody)));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("zzzz.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.SymbolNotFound);
        lookup.Price.Should().BeNull();
    }

    /// <summary>
    /// The download path's real answer for a symbol it will not serve: the words "Access denied" with
    /// HTTP <b>200</b>. Reported as a symbol that was not found, so a mistyped ticker does not tell the
    /// participant the whole service is down.
    /// </summary>
    [Fact]
    public async Task GetQuoteAsync_AccessDeniedWithASuccessStatus_ReturnsSymbolNotFound()
    {
        StooqClient client = Create(new StubHandler(Responds(HttpStatusCode.OK, "Access denied")));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aavvf.uss"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.SymbolNotFound);
        lookup.Price.Should().BeNull();
    }

    /// <summary>The daily history read end to end through the client, newest session quoted.</summary>
    [Fact]
    public async Task GetQuoteAsync_DailyHistory_ReturnsTheNewestSessionsClose()
    {
        const string history =
            "Date,Open,High,Low,Close,Volume\n"
            + "2026-08-04,7.78,7.8,7.68,7.68,136000\n"
            + "2026-08-05,7.51,7.71,7.5,7.69,1250\n";

        StooqClient client = Create(new StubHandler(Responds(HttpStatusCode.OK, history)));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aavvf.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.Quoted);
        lookup.Price.Should().Be(7.69m);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetQuoteAsync_NonSuccessStatus_ReturnsLookupFailed(HttpStatusCode status)
    {
        StooqClient client = Create(new StubHandler(Responds(status, QuoteBody)));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
        lookup.Price.Should().BeNull();
    }

    /// <summary>
    /// Measured against the live service on 2026-08-05: the quote path answers <c>404</c> with a short
    /// HTML page. This covers the same page arriving with a success status, which a proxy or a bot
    /// challenge can produce.
    /// </summary>
    [Fact]
    public async Task GetQuoteAsync_HtmlErrorPage_ReturnsLookupFailed()
    {
        const string html = "<meta charset=utf-8><title>Stooq</title><p>The page you requested does not exist";
        StooqClient client = Create(new StubHandler(Responds(HttpStatusCode.OK, html)));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    [Fact]
    public async Task GetQuoteAsync_EmptyBody_ReturnsLookupFailed()
    {
        StooqClient client = Create(new StubHandler(Responds(HttpStatusCode.OK, string.Empty)));

        StockQuoteLookup lookup = await client.GetQuoteAsync(Code("aapl.us"), CancellationToken.None);

        lookup.Outcome.Should().Be(StockQuoteOutcome.LookupFailed);
    }

    [Fact]
    public async Task GetQuoteAsync_TransportFailure_ReturnsLookupFailed()
    {
        StooqClient client = Create(new StubHandler((_, _) =>
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
        StooqClient client = Create(new StubHandler((_, _) =>
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
        StooqClient client = Create(new StubHandler((_, _) =>
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

        StooqClient client = Create(new StubHandler(Responds(HttpStatusCode.OK, QuoteBody)));

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

        StooqClient client = Create(handler);

        Func<Task> lookup = () => client.GetQuoteAsync(Code("aapl.us"), cancellation.Token);

        await lookup.Should().ThrowAsync<OperationCanceledException>();
        transportSawTheCancellation.Should().BeTrue();
    }

    [Fact]
    public async Task GetQuoteAsync_NullStockCode_Throws()
    {
        StooqClient client = Create(new StubHandler(Responds(HttpStatusCode.OK, QuoteBody)));

        Func<Task> lookup = () => client.GetQuoteAsync(null!, CancellationToken.None);

        await lookup.Should().ThrowAsync<ArgumentNullException>();
    }

    private static StooqClient Create(StubHandler handler)
    {
        HttpClient httpClient = new(handler, disposeHandler: true);

        return new StooqClient(httpClient, Options.Create(Settings), NullLogger<StooqClient>.Instance);
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
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/csv") };

    /// <summary>Records the outbound request and answers with whatever the test dictates.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            return respond(request, cancellationToken);
        }
    }
}
