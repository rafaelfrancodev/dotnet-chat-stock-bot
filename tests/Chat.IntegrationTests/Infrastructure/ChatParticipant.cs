using System.Text.RegularExpressions;

namespace Chat.IntegrationTests.Infrastructure;

/// <summary>
/// One participant driving the application the way a browser would: the real Identity pages, the real
/// antiforgery tokens, the real authentication cookie.
/// </summary>
/// <remarks>
/// <b>No test-only authentication scheme exists in this solution.</b> Registering and logging in through
/// the shipped Razor Pages costs a few extra requests and gives the suite the thing that actually matters:
/// the identity the hub reads comes from a cookie the application itself issued, including the
/// <c>display_name</c> claim <c>DisplayNameClaimsPrincipalFactory</c> adds at sign-in. A test
/// authentication handler would have proven that a fake principal reaches the hub, and — if it were ever
/// reachable from <c>Chat.Web</c> — would be a genuine security hole. The cookie is carried into
/// <c>HubConnection</c> as a request header, because a SignalR client cannot share this
/// <see cref="HttpMessageHandler"/> chain.
/// </remarks>
public sealed partial class ChatParticipant : IDisposable
{
    /// <summary>
    /// Password every test account uses. Not a secret: it exists only inside a throwaway container's
    /// database and only has to satisfy Identity's default policy.
    /// </summary>
    public const string Password = "Integration!Test1";

    private const string RegisterPath = "/Identity/Account/Register";
    private const string LoginPath = "/Identity/Account/Login";

    private readonly CookieJarHandler cookies;
    private readonly Uri baseAddress;

    internal ChatParticipant(HttpClient http, CookieJarHandler cookies, Uri baseAddress, string email, string displayName)
    {
        Http = http;
        Email = email;
        DisplayName = displayName;
        this.cookies = cookies;
        this.baseAddress = baseAddress;
    }

    /// <summary>This participant's browser: cookies survive across requests, redirects are not followed.</summary>
    public HttpClient Http { get; }

    /// <summary>Identity user name, unique per test.</summary>
    public string Email { get; }

    /// <summary>Name rendered as the post owner, captured by the registration page.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// The current session as a <c>Cookie</c> header, ready to be attached to a SignalR connection.
    /// </summary>
    public string CookieHeader => cookies.CookieHeader(baseAddress);

    /// <summary>Registers the account through the real registration page, which also signs it in.</summary>
    public Task<HttpResponseMessage> RegisterAsync() =>
        SubmitFormAsync(RegisterPath, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Input.DisplayName"] = DisplayName,
            ["Input.Email"] = Email,
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password,
        });

    /// <summary>Signs in through the real login page.</summary>
    public Task<HttpResponseMessage> LogInAsync() =>
        SubmitFormAsync(LoginPath, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Input.Email"] = Email,
            ["Input.Password"] = Password,
            ["Input.RememberMe"] = "false",
        });

    /// <inheritdoc/>
    public void Dispose() => Http.Dispose();

    /// <summary>
    /// Fills in one Razor Pages form: fetch it, take its antiforgery token, post it back. Skipping the GET
    /// would make every POST a 400, because the application's antiforgery protection is real.
    /// </summary>
    private async Task<HttpResponseMessage> SubmitFormAsync(string path, Dictionary<string, string> fields)
    {
        using HttpResponseMessage page = await Http.GetAsync(path).ConfigureAwait(false);
        page.EnsureSuccessStatusCode();

        string html = await page.Content.ReadAsStringAsync().ConfigureAwait(false);
        fields["__RequestVerificationToken"] = AntiforgeryToken(html);

        using FormUrlEncodedContent form = new(fields);

        return await Http.PostAsync(path, form).ConfigureAwait(false);
    }

    private static string AntiforgeryToken(string html)
    {
        Match match = AntiforgeryTokenPattern().Match(html);

        return match.Success
            ? match.Groups["token"].Value
            : throw new InvalidOperationException("The page carried no antiforgery token, so it cannot be posted.");
    }

    [GeneratedRegex(
        """name="__RequestVerificationToken"[^>]*?value="(?<token>[^"]+)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenPattern();
}
