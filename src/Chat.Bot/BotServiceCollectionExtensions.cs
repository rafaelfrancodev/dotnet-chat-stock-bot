using Chat.Application;
using Chat.Application.Abstractions.Hosting;
using Chat.Infrastructure;
using Chat.Infrastructure.HealthChecks;
using Chat.Infrastructure.Messaging;

namespace Chat.Bot;

/// <summary>
/// Everything the bot process registers, in one place so it can be asserted rather than described.
/// </summary>
/// <remarks>
/// The challenge's decoupling requirement is a statement about this composition: the bot must reach the
/// chat only through the broker, never through the database. A comment in <c>Program.cs</c> saying so is
/// not enforcement — a top-level statement file cannot be called from a test, so adding
/// <c>AddPersistence</c> there would compile, run and break nothing. Extracting the registrations makes
/// the claim testable, which is what <c>BotCompositionTests</c> does.
/// </remarks>
internal static class BotServiceCollectionExtensions
{
    /// <summary>
    /// Registers the bot's use cases, its broker consumer, its quote provider and its health probes.
    /// </summary>
    /// <param name="services">Service collection to register into.</param>
    /// <param name="configuration">Configuration carrying the broker and quote-provider settings.</param>
    /// <returns>The same collection, for chaining.</returns>
    internal static IServiceCollection AddBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // IBotFeature scopes the handler scan to the bot's own use cases, so the shared Application
        // assembly never asks this host to construct a handler that needs the database it deliberately
        // does not have.
        services.AddApplication<IBotFeature>();
        services.AddSystemClock();

        // The bus is this host's worker: MassTransit's hosted service owns the connection and hands every
        // StockQuoteRequested to the consumer below, so no BackgroundService of our own is needed. The
        // extension — never AddConsumer<T>() — is what pins the endpoint to MessagingConstants'
        // "stock-quote-requests" instead of a name derived from the consumer's class name.
        services.AddMessaging(
            configuration,
            configurator => configurator.AddStockQuoteRequestConsumer<StockQuoteRequestConsumer>());

        services.AddStockQuotes(configuration);

        // No database probe: the bot has no persistence by design, which is what keeps it decoupled.
        services.AddHealthChecks()
            .AddChatBroker(configuration)
            .AddStockQuoteProvider(configuration);

        return services;
    }
}
