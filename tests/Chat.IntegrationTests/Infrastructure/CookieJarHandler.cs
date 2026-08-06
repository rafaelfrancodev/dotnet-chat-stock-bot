using System.Net;

namespace Chat.IntegrationTests.Infrastructure;

/// <summary>
/// Keeps cookies across requests made through <c>TestServer</c>, and exposes them so the same session can
/// be handed to a SignalR client.
/// </summary>
/// <remarks>
/// <c>TestServer.CreateHandler()</c> is an <see cref="HttpMessageHandler"/>, not an
/// <see cref="HttpClientHandler"/>, so it has no <see cref="CookieContainer"/> of its own: without this
/// handler the antiforgery cookie would not survive the GET→POST of a Razor Pages form and no login could
/// complete. Owning the container (rather than letting <c>WebApplicationFactory.CreateClient</c> own it)
/// is also what makes <see cref="CookieHeader"/> possible — the authentication cookie has to travel to
/// <c>HubConnection</c>, which cannot share this handler.
/// </remarks>
internal sealed class CookieJarHandler : DelegatingHandler
{
    private readonly CookieContainer cookies = new();

    /// <summary>
    /// The current session as a <c>Cookie</c> request-header value, or an empty string when there is none.
    /// </summary>
    /// <param name="requestUri">Address the cookies will be sent to; scopes them exactly as a browser would.</param>
    public string CookieHeader(Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);

        return cookies.GetCookieHeader(requestUri);
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Uri requestUri = request.RequestUri
            ?? throw new InvalidOperationException("A test request must carry an absolute URI.");

        string header = cookies.GetCookieHeader(requestUri);

        if (!string.IsNullOrEmpty(header))
        {
            request.Headers.Remove("Cookie");
            request.Headers.Add("Cookie", header);
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies))
        {
            foreach (string setCookie in setCookies)
            {
                cookies.SetCookies(requestUri, setCookie);
            }
        }

        return response;
    }
}
