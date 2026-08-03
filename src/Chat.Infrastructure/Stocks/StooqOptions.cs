namespace Chat.Infrastructure.Stocks;

/// <summary>
/// Settings for the Stooq quote provider. Bound from the <c>Stooq</c> configuration section.
/// </summary>
public sealed class StooqOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Stooq";

    /// <summary>Root address of the quote service.</summary>
    public Uri BaseAddress { get; init; } = new("https://stooq.com/");

    /// <summary>Relative quote path; <c>{0}</c> is replaced with the validated stock code.</summary>
    public string QuotePath { get; init; } = "q/l/?s={0}&f=sd2t2ohlcv&h&e=csv";

    /// <summary>Request timeout applied to the typed HTTP client.</summary>
    public int TimeoutSeconds { get; init; } = 10;
}
