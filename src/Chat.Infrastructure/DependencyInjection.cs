using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Messaging;
using Chat.Infrastructure.Messaging;
using Chat.Infrastructure.Persistence;
using Chat.Infrastructure.Persistence.Repositories;
using Chat.Infrastructure.Time;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure;

/// <summary>
/// Composition entry point for the Infrastructure layer. Split per concern so a host can opt in to
/// only what it needs: Chat.Web needs persistence + Identity + messaging, Chat.Bot needs messaging + Stooq.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// EF Core, Identity stores and repository implementations. Used by Chat.Web only —
    /// Chat.Bot never calls this, which is what structurally keeps the bot away from the database.
    /// </summary>
    /// <param name="services">Service collection to register into.</param>
    /// <param name="configuration">
    /// Configuration carrying <c>ConnectionStrings:ChatDatabase</c>, supplied by user-secrets or the
    /// <c>ConnectionStrings__ChatDatabase</c> environment variable — never by a committed file.
    /// </param>
    /// <exception cref="InvalidOperationException">The connection string is missing or blank.</exception>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string? connectionString = configuration.GetConnectionString(PersistenceConstants.ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Fails fast, unlike the health check's equivalent gap: a host that cannot reach its
            // database cannot serve a single request, so starting up and failing later would only
            // hide the misconfiguration behind a stack trace on the first message.
            throw new InvalidOperationException(
                $"ConnectionStrings:{PersistenceConstants.ConnectionStringName} is not configured. Set it with " +
                $"\"dotnet user-secrets set \"ConnectionStrings:{PersistenceConstants.ConnectionStringName}\" " +
                "\"<connection string>\" --project src/Chat.Web\" or the " +
                $"ConnectionStrings__{PersistenceConstants.ConnectionStringName} environment variable. " +
                "See README -> Configuration.");
        }

        services.AddDbContext<ChatDbContext>(options =>
            options.UseSqlServer(connectionString, sqlServer => sqlServer.EnableRetryOnFailure(
                PersistenceConstants.MaxRetryCount,
                TimeSpan.FromSeconds(PersistenceConstants.MaxRetryDelaySeconds),
                errorNumbersToAdd: null)));

        services.AddScoped<IChatRoomRepository, ChatRoomRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }

    /// <summary>
    /// The system clock behind <see cref="IDateTimeProvider"/>. Registered by every host that creates
    /// domain objects, because the aggregates take their timestamp as a parameter instead of reading it.
    /// </summary>
    /// <param name="services">Service collection to register into.</param>
    public static IServiceCollection AddSystemClock(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        return services;
    }

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
