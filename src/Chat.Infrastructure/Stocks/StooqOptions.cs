using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Stocks;

/// <summary>
/// Settings for the Stooq quote provider. Bound from the <c>Stooq</c> configuration section, and validated
/// on start so a mistyped endpoint fails at the host rather than inside a consumer.
/// </summary>
public sealed class StooqOptions : IValidatableObject
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Stooq";

    /// <summary>Root address of the quote service.</summary>
    [Required]
    public Uri BaseAddress { get; init; } = new("https://stooq.com/");

    /// <summary>
    /// Relative quote path; <c>{0}</c> is replaced with the validated stock code.
    /// </summary>
    /// <remarks>
    /// Defaults to the daily-history download, <c>q/d/l/</c>, whose newest row carries the last close.
    /// The single-quote path the challenge documents — <c>q/l/?s={0}&amp;f=sd2t2ohlcv&amp;h&amp;e=csv</c> —
    /// has been withdrawn: measured 2026-08-06, it answers <c>404</c> with an HTML page. Both shapes are
    /// understood by <c>StooqCsvParser</c>, so this is configuration, not a code change:
    /// set <c>Stooq:QuotePath</c> to point at either.
    /// <para>
    /// Note that <c>q/d/l/</c> is currently behind a JavaScript "verify your browser" proof-of-work gate,
    /// so it serves a browser but answers an HTTP client with the challenge page rather than CSV. See the
    /// README for the measurements and what that means for a reviewer.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string QuotePath { get; init; } = "q/d/l/?s={0}&i=d";

    /// <summary>
    /// Total budget for one lookup, spent across every attempt. Bounded because zero would abort the call
    /// before it left the process, and an unbounded value would hold a chat participant waiting.
    /// </summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 10;

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        StockQuoteOptionsValidation.Validate(SectionName, BaseAddress, QuotePath);
}
