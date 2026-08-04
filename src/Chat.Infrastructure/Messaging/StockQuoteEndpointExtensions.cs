using Chat.Application.Contracts.Messaging;
using MassTransit;

namespace Chat.Infrastructure.Messaging;

/// <summary>
/// Binds a host's consumer to the receive endpoint the topology reserves for it. Hosts call these from
/// the registration callback of <see cref="DependencyInjection.AddMessaging"/>.
/// </summary>
/// <remarks>
/// The endpoint names come from <see cref="MessagingConstants"/> and are applied here, in one place, so
/// a host cannot invent a queue name and cannot silently drift from the kebab-case convention. Without
/// this, <c>ConfigureEndpoints</c> would derive a name from the consumer's class name
/// (<c>StockQuoteRequestConsumer</c> becomes <c>stock-quote-request</c>), which is neither the documented
/// topology nor stable across a rename.
/// <para>
/// Each host registers only its own side — Chat.Bot the request consumer, Chat.Web the response
/// consumer — so neither host learns the other's endpoint.
/// </para>
/// </remarks>
public static class StockQuoteEndpointExtensions
{
    /// <summary>
    /// Registers the Chat.Bot consumer that resolves quotes, on
    /// <see cref="MessagingConstants.StockQuoteRequestQueue"/>. Competing consumers: any bot instance
    /// may serve a request.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer handling <see cref="StockQuoteRequested"/>.</typeparam>
    /// <param name="configurator">MassTransit's bus registration configurator.</param>
    public static IBusRegistrationConfigurator AddStockQuoteRequestConsumer<TConsumer>(
        this IBusRegistrationConfigurator configurator)
        where TConsumer : class, IConsumer<StockQuoteRequested> =>
        AddConsumerOnEndpoint<TConsumer>(configurator, MessagingConstants.StockQuoteRequestQueue);

    /// <summary>
    /// Registers the Chat.Web consumer that posts the bot's answer, on
    /// <see cref="MessagingConstants.StockQuoteResponseQueue"/>.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer handling <see cref="StockQuoteResolved"/>.</typeparam>
    /// <param name="configurator">MassTransit's bus registration configurator.</param>
    public static IBusRegistrationConfigurator AddStockQuoteResponseConsumer<TConsumer>(
        this IBusRegistrationConfigurator configurator)
        where TConsumer : class, IConsumer<StockQuoteResolved> =>
        AddConsumerOnEndpoint<TConsumer>(configurator, MessagingConstants.StockQuoteResponseQueue);

    private static IBusRegistrationConfigurator AddConsumerOnEndpoint<TConsumer>(
        IBusRegistrationConfigurator configurator,
        string endpointName)
        where TConsumer : class, IConsumer
    {
        ArgumentNullException.ThrowIfNull(configurator);

        configurator
            .AddConsumer<TConsumer>()
            .Endpoint(endpoint => endpoint.Name = endpointName);

        return configurator;
    }
}
