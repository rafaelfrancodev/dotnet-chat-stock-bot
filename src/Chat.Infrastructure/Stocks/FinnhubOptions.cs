using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Stocks;

/// <summary>
/// Settings for the Finnhub quote provider. Bound from the <c>Finnhub</c> configuration section.
/// </summary>
/// <remarks>
/// Finnhub is an API built for programmatic access, which is why it is the alternative to Stooq rather
/// than something that works around Stooq's browser check: it answers an <c>HttpClient</c> by design.
/// <para>
/// Validated on start. <see cref="ApiKey"/> is deliberately <b>not</b> required: running without a key is a
/// supported degraded mode — the bot answers a friendly failure and the health check reports the gap — so
/// a missing key must not stop the host.
/// </para>
/// </remarks>
public sealed class FinnhubOptions : IValidatableObject
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Finnhub";

    /// <summary>
    /// Header Finnhub reads the API key from, used instead of the <c>token</c> query parameter.
    /// </summary>
    /// <remarks>
    /// A credential in a query string is a credential in every log, proxy access log and browser history
    /// that ever sees the URL. Nothing here leaked it today — <c>IHttpClientFactory</c>'s request log
    /// redacts query values — but that safety depends on the <c>System.Net.Http.DisableUriRedaction</c>
    /// switch staying off and on nobody enabling extended HTTP logging. A header does not need the
    /// framework to protect it.
    /// </remarks>
    public const string ApiKeyHeader = "X-Finnhub-Token";

    /// <summary>Root address of the quote service.</summary>
    [Required]
    public Uri BaseAddress { get; init; } = new("https://finnhub.io/");

    /// <summary>Relative quote path; <c>{0}</c> is the symbol.</summary>
    /// <remarks>
    /// The key is deliberately absent from this template. Finnhub accepts it either as a <c>token</c> query
    /// parameter or as the <see cref="ApiKeyHeader"/> header, and the header keeps a credential out of the
    /// URL entirely — see <see cref="ApiKey"/>.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string QuotePath { get; init; } = "api/v1/quote?symbol={0}";

    /// <summary>
    /// Total budget for one lookup, spent across every attempt. Bounded because zero would abort the call
    /// before it left the process, and an unbounded value would hold a chat participant waiting.
    /// </summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// The API key. Supplied by user-secrets or the <c>Finnhub__ApiKey</c> environment variable, never by
    /// a committed file — it is a credential like the database password, and it is sent as the
    /// <see cref="ApiKeyHeader"/> header rather than in the query string.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        StockQuoteOptionsValidation.Validate(SectionName, BaseAddress, QuotePath);
}
