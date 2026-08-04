using Chat.Application.Contracts.Messaging;

namespace Chat.Application.Abstractions.Stocks;

/// <summary>
/// Hands a stock lookup to the bot. Used by Chat.Web when a participant types <c>/stock=code</c>;
/// implemented in Chat.Infrastructure over the message broker.
/// </summary>
/// <remarks>
/// This port is what keeps the bot decoupled, in both directions. Chat.Web knows only that a request
/// was published — it never calls Stooq, never waits for an answer, and never references the transport.
/// Chat.Application therefore takes no dependency on MassTransit.
/// </remarks>
public interface IStockQuoteRequester
{
    /// <summary>Publishes a quote request for the bot to pick up.</summary>
    /// <param name="request">The validated request. Its stock code has already passed <c>StockCode</c>.</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    Task RequestAsync(StockQuoteRequested request, CancellationToken cancellationToken = default);
}
