using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Chat.Infrastructure.Stocks;

/// <summary>
/// The endpoint checks both quote providers' options share, run at start by <c>ValidateOnStart</c>.
/// </summary>
/// <remarks>
/// Data annotations alone cannot express either rule. <c>[Required]</c> passes on a <b>relative</b>
/// <see cref="Uri"/>, because the framework's <c>UriTypeConverter</c> binds any string — measured:
/// <c>Finnhub:BaseAddress=not a uri</c> binds happily and only throws later, inside <c>Consume</c>, where
/// <c>new Uri(baseAddress, path)</c> rejects a relative base. And a quote path missing its <c>{0}</c>
/// placeholder would query the service with no symbol at all, which reads as a broken bot rather than a
/// mistyped setting.
/// </remarks>
internal static class StockQuoteOptionsValidation
{
    /// <summary>Placeholder every quote path must carry: the validated stock code.</summary>
    internal const string SymbolPlaceholder = "{0}";

    /// <summary>
    /// Validates the two settings that describe the outbound URL. Both provider options declare these
    /// exact property names, which is why the member names can be shared.
    /// </summary>
    /// <param name="sectionName">Configuration section, so the message names the key an operator must fix.</param>
    /// <param name="baseAddress">The configured service root.</param>
    /// <param name="quotePath">The configured relative quote path.</param>
    internal static IEnumerable<ValidationResult> Validate(string sectionName, Uri? baseAddress, string? quotePath)
    {
        if (baseAddress is not null && !baseAddress.IsAbsoluteUri)
        {
            yield return Failure(
                sectionName,
                nameof(FinnhubOptions.BaseAddress),
                $"must be an absolute URL such as \"https://finnhub.io/\", but is \"{baseAddress.OriginalString}\"");
        }

        if (!string.IsNullOrWhiteSpace(quotePath)
            && !quotePath.Contains(SymbolPlaceholder, StringComparison.Ordinal))
        {
            yield return Failure(
                sectionName,
                nameof(FinnhubOptions.QuotePath),
                $"must contain the \"{SymbolPlaceholder}\" placeholder the stock code is written into");
        }
    }

    private static ValidationResult Failure(string sectionName, string member, string requirement) =>
        new(
            string.Format(CultureInfo.InvariantCulture, "{0}:{1} {2}.", sectionName, member, requirement),
            [member]);
}
