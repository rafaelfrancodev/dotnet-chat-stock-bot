using Chat.Application.Abstractions.Stocks;
using Chat.Application.Contracts.Messaging;
using MassTransit;

namespace Chat.Infrastructure.Messaging;

/// <summary>
/// Publishes <see cref="StockQuoteRequested"/> so Chat.Bot can pick it up. The adapter that keeps
/// MassTransit on this side of the dependency rule: <c>Chat.Application</c> sees only
/// <see cref="IStockQuoteRequester"/>.
/// </summary>
/// <remarks>
/// <see cref="IPublishEndpoint"/> is resolved per scope, so inside a consumer the publish inherits that
/// message's <c>ConversationId</c> and correlation headers instead of starting a new conversation.
/// Publishing (rather than sending to a queue address) is deliberate: the producer names a message type,
/// not a destination, so adding a second subscriber later needs no change here.
/// </remarks>
/// <param name="publishEndpoint">MassTransit's publish surface for the current scope.</param>
internal sealed class MassTransitStockQuoteRequester(IPublishEndpoint publishEndpoint) : IStockQuoteRequester
{
    public Task RequestAsync(StockQuoteRequested request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return publishEndpoint.Publish(request, cancellationToken);
    }
}
