namespace Chat.Infrastructure.Stocks;

/// <summary>
/// Settings for the Finnhub quote provider. Bound from the <c>Finnhub</c> configuration section.
/// </summary>
/// <remarks>
/// Finnhub is an API built for programmatic access, which is why it is the alternative to Stooq rather
/// than something that works around Stooq's browser check: it answers an <c>HttpClient</c> by design.
/// </remarks>
public sealed class FinnhubOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Finnhub";

    /// <summary>Root address of the quote service.</summary>
    public Uri BaseAddress { get; init; } = new("https://finnhub.io/");

    /// <summary>Relative quote path; <c>{0}</c> is the symbol and <c>{1}</c> the API key.</summary>
    public string QuotePath { get; init; } = "api/v1/quote?symbol={0}&token={1}";

    /// <summary>Request timeout applied to the typed HTTP client.</summary>
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// The API key. Supplied by user-secrets or the <c>Finnhub__ApiKey</c> environment variable, never by
    /// a committed file — it is a credential like the database password.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;
}
