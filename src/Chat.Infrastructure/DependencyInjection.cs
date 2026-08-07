using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Stocks;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Messaging;
using Chat.Infrastructure.Messaging;
using Chat.Infrastructure.Persistence;
using Chat.Infrastructure.Persistence.Repositories;
using Chat.Infrastructure.Stocks;
using Chat.Infrastructure.Time;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure;

/// <summary>
/// Composition entry point for the Infrastructure layer. Split per concern so a host can opt in to
/// only what it needs: Chat.Web needs persistence + Identity + messaging, Chat.Bot needs messaging + Stooq.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Configuration key choosing which quote provider the bot uses. Defaults to
    /// <see cref="FinnhubProvider"/>.
    /// </summary>
    public const string StockQuoteProviderKey = "Stocks:Provider";

    /// <summary>
    /// Stooq's CSV endpoint — the service the challenge names. Kept and still selectable, but no longer the
    /// default: its CSV paths cannot be read from a server (404 on one, a browser proof-of-work check on
    /// the other), so it can only ever answer with a friendly failure.
    /// </summary>
    public const string StooqProvider = "Stooq";

    /// <summary>
    /// Finnhub's JSON quote API — the default, because it is the one that actually returns a price. It is
    /// built for programmatic access, so it answers an <c>HttpClient</c> rather than requiring a browser.
    /// Needs <c>Finnhub:ApiKey</c>; without it the bot logs the gap and answers as it would for any
    /// unreachable provider.
    /// </summary>
    public const string FinnhubProvider = "Finnhub";

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

        // Startup migration and the default room. Needs IDateTimeProvider, which the host registers
        // with AddSystemClock() — the clock is not persistence, so it stays a separate opt-in.
        services.AddScoped<ChatDbSeeder>();

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
    /// Host-specific consumer registration, expressed with
    /// <see cref="Messaging.StockQuoteEndpointExtensions"/>. Chat.Bot registers the request consumer,
    /// Chat.Web the response consumer; neither host learns about the other's endpoint.
    /// </param>
    /// <remarks>
    /// Also registers the two outbound ports over <see cref="IPublishEndpoint"/>, so every host that has
    /// a bus can publish without knowing the transport. Both are scoped, matching MassTransit's own
    /// lifetime for <see cref="IPublishEndpoint"/>: inside a consumer the scoped endpoint carries the
    /// current <c>ConsumeContext</c>, which is what propagates correlation across the round trip.
    /// <para>
    /// Registering the bus also registers MassTransit's <c>masstransit-bus</c> health check — see
    /// <c>HealthChecks/HealthCheckNames.cs</c> for what it does and does not report.
    /// </para>
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

                // Applies the settings above to every registered consumer's endpoint, using the
                // endpoint names StockQuoteEndpointExtensions pinned to MessagingConstants.
                bus.ConfigureEndpoints(context);
            });
        });

        services.TryAddScoped<IStockQuoteRequester, MassTransitStockQuoteRequester>();
        services.TryAddScoped<IStockQuoteResponder, MassTransitStockQuoteResponder>();

        return services;
    }

    /// <summary>
    /// The typed quote-provider HTTP client selected by <c>Stocks:Provider</c>, and its parsing. Used by
    /// Chat.Bot only.
    /// </summary>
    /// <param name="services">Service collection to register into.</param>
    /// <param name="configuration">
    /// Configuration carrying <c>Stocks:Provider</c> and the selected provider's own section.
    /// </param>
    /// <remarks>
    /// The client is typed and named, so <c>IHttpClientFactory</c> owns one pooled handler for the whole
    /// process — a new <see cref="HttpClient"/> per quote would exhaust sockets under the flood of
    /// <c>/stock=</c> commands the challenge warns about.
    /// <para>
    /// The selected provider's <c>TimeoutSeconds</c> is applied as the resilience pipeline's
    /// <c>TotalRequestTimeout</c> rather than as <see cref="HttpClient.Timeout"/>. Measured:
    /// <c>AddStandardResilienceHandler</c> appends its own client action setting
    /// <see cref="HttpClient.Timeout"/> to <see cref="Timeout.InfiniteTimeSpan"/>, so the pipeline owns the
    /// budget by design — setting the client timeout as well would only add a race able to abort a retry
    /// mid-flight. The per-attempt timeout is that budget divided by
    /// <see cref="Stocks.StockQuoteHttpDefaults.MaxAttemptsPerLookup"/>, so a hanging service leaves room
    /// for the retries instead of spending the whole budget on its first try.
    /// </para>
    /// <para>
    /// Both providers get the identical pipeline from <see cref="ConfigureQuoteResilience"/>: only the
    /// budget differs, and it comes from whichever options object was selected. Registration and probe read
    /// the selection through <see cref="StockQuoteProviderSelection"/>, so they cannot diverge.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddStockQuotes(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Validated on start, not on first use: a malformed BaseAddress or an out-of-range timeout would
        // otherwise surface inside Consume, where it costs four delivery attempts and a dead-lettered
        // request before anyone learns a setting was mistyped. Both sections are bound regardless of the
        // selection, so a typo in the section that is not in use still fails loudly.
        services
            .AddOptions<StooqOptions>()
            .Bind(configuration.GetSection(StooqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<FinnhubOptions>()
            .Bind(configuration.GetSection(FinnhubOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Resolved once, centrally, so the bot's health check probes the provider the bot actually calls.
        if (StockQuoteProviderSelection.Resolve(configuration) is StockQuoteProviderKind.Finnhub)
        {
            services
                .AddHttpClient<IStockQuoteProvider, FinnhubClient>(FinnhubClient.HttpClientName, ConfigureQuoteClient)
                .AddStandardResilienceHandler()
                .Configure((resilience, provider) => ConfigureQuoteResilience(
                    resilience,
                    provider.GetRequiredService<IOptions<FinnhubOptions>>().Value.TimeoutSeconds));

            return services;
        }

        services
            .AddHttpClient<IStockQuoteProvider, StooqClient>(StooqClient.HttpClientName, ConfigureQuoteClient)
            .AddStandardResilienceHandler()
            .Configure((resilience, provider) => ConfigureQuoteResilience(
                resilience,
                provider.GetRequiredService<IOptions<StooqOptions>>().Value.TimeoutSeconds));

        return services;
    }

    /// <summary>
    /// Bounds the buffered response. The base address is deliberately not set here: each client builds an
    /// absolute URL from its own options, so there is one source for the endpoint.
    /// </summary>
    private static void ConfigureQuoteClient(HttpClient client) =>
        client.MaxResponseContentBufferSize = StockQuoteHttpDefaults.MaxResponseBytes;

    /// <summary>
    /// The one resilience pipeline both quote providers get. Only <paramref name="timeoutSeconds"/> differs
    /// between them, which is why this takes the budget rather than an options object — a provider-specific
    /// copy of this method is how one provider's pipeline ends up keyed off another's constants.
    /// </summary>
    /// <param name="resilience">The standard handler's options for the client being registered.</param>
    /// <param name="timeoutSeconds">Total budget for one lookup, from the selected provider's options.</param>
    private static void ConfigureQuoteResilience(HttpStandardResilienceOptions resilience, int timeoutSeconds)
    {
        TimeSpan budget = TimeSpan.FromSeconds(timeoutSeconds);
        TimeSpan perAttempt = budget / StockQuoteHttpDefaults.MaxAttemptsPerLookup;

        resilience.TotalRequestTimeout.Timeout = budget;
        resilience.AttemptTimeout.Timeout = perAttempt;
        resilience.Retry.MaxRetryAttempts = StockQuoteHttpDefaults.MaxAttemptsPerLookup - 1;
        resilience.Retry.Delay = TimeSpan.FromMilliseconds(StockQuoteHttpDefaults.RetryDelayMilliseconds);

        // The standard handler validates that the breaker samples at least two attempts; a generous
        // configured timeout would otherwise fail validation at startup rather than at the first call.
        TimeSpan minimumSamplingDuration = perAttempt * 2;

        if (resilience.CircuitBreaker.SamplingDuration < minimumSamplingDuration)
        {
            resilience.CircuitBreaker.SamplingDuration = minimumSamplingDuration;
        }
    }
}
