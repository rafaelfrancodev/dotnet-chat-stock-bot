using Chat.Domain.StockCommands;

namespace Chat.Application.Abstractions.Stocks;

/// <summary>
/// The bot's port onto a quote service. Implemented in Chat.Infrastructure by the Stooq typed client.
/// </summary>
public interface IStockQuoteProvider
{
    /// <summary>Looks up the latest closing price for a ticker.</summary>
    /// <remarks>
    /// Implementations must not throw for an unknown symbol, a timeout or an unparseable response —
    /// those are <see cref="StockQuoteLookup"/> outcomes. The bot answers every request, so a provider
    /// that threw would turn a third-party hiccup into silence in the chat room.
    /// <para>
    /// The parameter is a <see cref="StockCode"/>, not a string: it has already passed the allow-list,
    /// so nothing the implementation interpolates into a URL can carry injection characters.
    /// </para>
    /// </remarks>
    /// <param name="stockCode">The validated ticker to look up.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<StockQuoteLookup> GetQuoteAsync(StockCode stockCode, CancellationToken cancellationToken = default);
}
