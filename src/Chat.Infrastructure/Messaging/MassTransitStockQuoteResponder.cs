using Chat.Application.Abstractions.Stocks;
using Chat.Application.Contracts.Messaging;
using MassTransit;

namespace Chat.Infrastructure.Messaging;

/// <summary>
/// Publishes <see cref="StockQuoteResolved"/> so Chat.Web can post the bot's answer. Mirror image of
/// <see cref="MassTransitStockQuoteRequester"/>: the bot answers through the broker, never by calling
/// Chat.Web, which is what the challenge means by a decoupled bot.
/// </summary>
/// <param name="publishEndpoint">MassTransit's publish surface for the current scope.</param>
internal sealed class MassTransitStockQuoteResponder(IPublishEndpoint publishEndpoint) : IStockQuoteResponder
{
    public Task RespondAsync(StockQuoteResolved response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        return publishEndpoint.Publish(response, cancellationToken);
    }
}
