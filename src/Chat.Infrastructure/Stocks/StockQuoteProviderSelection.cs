using Microsoft.Extensions.Configuration;

namespace Chat.Infrastructure.Stocks;

/// <summary>The quote provider a configuration selects.</summary>
internal enum StockQuoteProviderKind
{
    /// <summary>Finnhub's JSON API. The default — see <see cref="DependencyInjection.FinnhubProvider"/>.</summary>
    Finnhub,

    /// <summary>Stooq's CSV endpoint — see <see cref="DependencyInjection.StooqProvider"/>.</summary>
    Stooq,
}

/// <summary>
/// Reads <c>Stocks:Provider</c>. The single place that decides which provider is configured.
/// </summary>
/// <remarks>
/// Shared deliberately: the quote client and the health check must agree on the answer. When each read the
/// setting for itself, <c>/health</c> reported Stooq while the bot was calling Finnhub — a probe of a
/// service the process never uses is worse than no probe, because it reports green for the wrong thing.
/// </remarks>
internal static class StockQuoteProviderSelection
{
    /// <summary>Resolves the configured provider.</summary>
    /// <param name="configuration">Configuration carrying <c>Stocks:Provider</c>.</param>
    /// <exception cref="InvalidOperationException">The value names no known provider.</exception>
    public static StockQuoteProviderKind Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string provider = configuration[DependencyInjection.StockQuoteProviderKey]
            ?? DependencyInjection.FinnhubProvider;

        if (provider.Equals(DependencyInjection.FinnhubProvider, StringComparison.OrdinalIgnoreCase))
        {
            return StockQuoteProviderKind.Finnhub;
        }

        if (provider.Equals(DependencyInjection.StooqProvider, StringComparison.OrdinalIgnoreCase))
        {
            return StockQuoteProviderKind.Stooq;
        }

        // Fail fast rather than silently falling back: a typo in the provider name would otherwise look
        // like the alternative provider quietly not being used.
        throw new InvalidOperationException(
            $"{DependencyInjection.StockQuoteProviderKey} is \"{provider}\", which is not a known quote "
            + $"provider. Use \"{DependencyInjection.StooqProvider}\" or "
            + $"\"{DependencyInjection.FinnhubProvider}\".");
    }
}
