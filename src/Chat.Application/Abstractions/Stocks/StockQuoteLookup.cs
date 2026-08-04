using Chat.Application.Contracts.Messaging;

namespace Chat.Application.Abstractions.Stocks;

/// <summary>
/// What a quote provider found. Reuses <see cref="StockQuoteOutcome"/> rather than introducing a second
/// vocabulary for the same three cases, so the bot can map a lookup onto its published answer directly.
/// </summary>
/// <remarks>
/// There is no failure case beyond the enum: an unreachable or unparseable provider is
/// <see cref="StockQuoteOutcome.LookupFailed"/>, a normal answer the bot reports politely, not an
/// exception the caller has to catch.
/// </remarks>
/// <param name="Outcome">Why the lookup ended the way it did.</param>
/// <param name="Price">The closing price, set only when <paramref name="Outcome"/> is <see cref="StockQuoteOutcome.Quoted"/>.</param>
public sealed record StockQuoteLookup(StockQuoteOutcome Outcome, decimal? Price)
{
    /// <summary>A successful lookup carrying the closing price.</summary>
    /// <param name="price">The closing price reported by the provider.</param>
    public static StockQuoteLookup Quoted(decimal price) => new(StockQuoteOutcome.Quoted, price);

    /// <summary>The provider answered, but does not know the symbol.</summary>
    public static StockQuoteLookup SymbolNotFound { get; } = new(StockQuoteOutcome.SymbolNotFound, Price: null);

    /// <summary>The provider was unreachable, timed out, or returned something unusable.</summary>
    public static StockQuoteLookup LookupFailed { get; } = new(StockQuoteOutcome.LookupFailed, Price: null);
}
