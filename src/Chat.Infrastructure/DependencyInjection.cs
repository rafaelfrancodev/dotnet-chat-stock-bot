using Chat.Application.Contracts.Messaging;
using Chat.Infrastructure.Messaging;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure;

/// <summary>
/// Composition entry point for the Infrastructure layer. Split per concern so a host can opt in to
/// only what it needs: Chat.Web needs persistence + Identity + messaging, Chat.Bot needs messaging + Stooq.
/// </summary>
public static class DependencyInjection
{
    /// <summary>EF Core, Identity stores and repository implementations. Used by Chat.Web only.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration) => services;

    /// <summary>
    /// The MassTransit bus over RabbitMQ. Used by both hosts.
    /// </summary>
    /// <param name="services">Service collection to register into.</param>
    /// <param name="configuration">Configuration carrying the <c>RabbitMq</c> section.</param>
    /// <param name="registerConsumers">
    /// Host-specific consumer registration. Chat.Bot registers the request consumer, Chat.Web the
    /// response consumer; neither host learns about the other's endpoint.
    /// </param>
    /// <remarks>
    /// Registering the bus also registers MassTransit's <c>masstransit-bus</c> health check, which is
    /// how both hosts report broker connectivity — see <c>HealthChecks/HealthCheckNames.cs</c>.
    /// </remarks>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? registerConsumers = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName));

        services.AddMassTransit(configurator =>
        {
            // Queue names come from MessagingConstants, so kebab-case formatting keeps any
            // convention-named endpoint consistent with the ones we name explicitly.
            configurator.SetKebabCaseEndpointNameFormatter();

            registerConsumers?.Invoke(configurator);

            configurator.UsingRabbitMq((context, bus) =>
            {
                RabbitMqOptions options = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

                bus.Host(options.HostName, (ushort)options.Port, options.VirtualHost, host =>
                {
                    host.Username(options.UserName);
                    host.Password(options.Password);
                });

                bus.PrefetchCount = MessagingConstants.PrefetchCount;

                // Retry in place for transient faults, then let MassTransit move the message to
                // <queue>_error. Redelivering forever would turn one poison message into a hot loop.
                bus.UseMessageRetry(retry => retry.Interval(
                    MessagingConstants.RetryLimit,
                    TimeSpan.FromSeconds(MessagingConstants.RetryIntervalSeconds)));

                bus.ConfigureEndpoints(context);
            });
        });

        return services;
    }

    /// <summary>Typed Stooq HTTP client and CSV parsing. Used by Chat.Bot only.</summary>
    public static IServiceCollection AddStockQuotes(this IServiceCollection services, IConfiguration configuration) => services;
}
