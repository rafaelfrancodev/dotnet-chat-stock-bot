using System.Text.RegularExpressions;
using Chat.Domain.Common;

namespace Chat.Domain.StockCommands;

/// <summary>
/// A Stooq ticker symbol such as <c>aapl.us</c>, normalised to lower case.
/// <para>
/// This type is the security boundary for the outbound Stooq call: the only way to obtain one is
/// <see cref="Create"/>, which enforces a strict allow-list, so a value that reaches URL
/// construction can never carry <c>&amp;</c>, <c>?</c>, <c>/</c>, <c>=</c>, <c>%</c>, <c>#</c> or
/// whitespace. The combined rule is <c>^[a-z0-9.\-]{1,20}$</c>.
/// </para>
/// </summary>
public sealed partial record StockCode
{
    /// <summary>Upper bound on a ticker, suffix included (for example <c>aapl.us</c>).</summary>
    public const int MaxLength = 20;

    private StockCode(string value) => Value = value;

    /// <summary>The normalised, lower-case ticker. Safe to interpolate into the Stooq query string.</summary>
    public string Value { get; }

    /// <summary>
    /// The upper-case form used in chat output, as required by the challenge wording
    /// (<c>"AAPL.US quote is $93.42 per share"</c>).
    /// </summary>
    public string Display => Value.ToUpperInvariant();

    /// <summary>
    /// Validates and normalises <paramref name="value"/>: surrounding whitespace is removed, the
    /// ticker is lower-cased with invariant casing and must then match the allow-list.
    /// </summary>
    public static Result<StockCode> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return Result.Failure<StockCode>(Errors.Empty);
        }

        // Invariant casing on purpose: culture-sensitive lowering maps 'I' to 'ı' under tr-TR,
        // which would silently produce a ticker Stooq does not know.
        string normalised = trimmed.ToLowerInvariant();

        if (normalised.Length > MaxLength)
        {
            return Result.Failure<StockCode>(Errors.TooLong);
        }

        if (!AllowedCharacters().IsMatch(normalised))
        {
            return Result.Failure<StockCode>(Errors.InvalidFormat);
        }

        return Result.Success(new StockCode(normalised));
    }

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>
    /// Allow-list of the characters a ticker may contain — never a deny-list, so anything unforeseen
    /// is rejected by default. Anchored with <c>\A</c>/<c>\z</c> rather than <c>^</c>/<c>$</c>
    /// because in .NET <c>$</c> also matches before a trailing newline.
    /// </summary>
    [GeneratedRegex(@"\A[a-z0-9.\-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedCharacters();

    /// <summary>Expected failures of <see cref="Create"/>, with stable codes for tests and clients.</summary>
    public static class Errors
    {
        /// <summary>The ticker was null, empty or whitespace only.</summary>
        public static readonly Error Empty =
            Error.Validation("StockCode.Empty", "A stock code cannot be empty.");

        /// <summary>The ticker exceeded the maximum length.</summary>
        public static readonly Error TooLong = Error.Validation(
            "StockCode.TooLong",
            $"A stock code cannot exceed {MaxLength} characters.");

        /// <summary>The ticker contained a character outside the allow-list.</summary>
        public static readonly Error InvalidFormat = Error.Validation(
            "StockCode.InvalidFormat",
            "A stock code may contain only letters, digits, dots and hyphens (for example aapl.us).");
    }
}
