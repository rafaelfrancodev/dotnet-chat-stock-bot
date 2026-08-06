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

    /// <summary>Typed Stooq HTTP client and CSV parsing. Used by Chat.Bot only.</summary>
    /// <param name="services">Service collection to register into.</param>
    /// <param name="configuration">Configuration carrying the <c>Stooq</c> section.</param>
    /// <remarks>
    /// The client is typed and named, so <c>IHttpClientFactory</c> owns one pooled handler for the whole
    /// process — a new <see cref="HttpClient"/> per quote would exhaust sockets under the flood of
    /// <c>/stock=</c> commands the challenge warns about.
    /// <para>
    /// <c>StooqOptions.TimeoutSeconds</c> is applied as the resilience pipeline's <c>TotalRequestTimeout</c>
    /// rather than as <see cref="HttpClient.Timeout"/>. Measured: <c>AddStandardResilienceHandler</c>
    /// appends its own client action setting <see cref="HttpClient.Timeout"/> to
    /// <see cref="Timeout.InfiniteTimeSpan"/>, so the pipeline owns the budget by design — setting the
    /// client timeout as well would only add a race able to abort a retry mid-flight. The per-attempt
    /// timeout is that budget divided by <see cref="Stocks.StooqClient.MaxAttemptsPerLookup"/>, so a
    /// hanging Stooq leaves room for the retries instead of spending the whole budget on its first try.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddStockQuotes(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<StooqOptions>()
            .Bind(configuration.GetSection(StooqOptions.SectionName));

        services
            .AddOptions<FinnhubOptions>()
            .Bind(configuration.GetSection(FinnhubOptions.SectionName));

        string provider = configuration[StockQuoteProviderKey] ?? FinnhubProvider;

        if (provider.Equals(FinnhubProvider, StringComparison.OrdinalIgnoreCase))
        {
            services
                .AddHttpClient<IStockQuoteProvider, FinnhubClient>(FinnhubClient.HttpClientName, ConfigureFinnhubClient)
                .AddStandardResilienceHandler()
                .Configure(ConfigureFinnhubResilience);

            return services;
        }

        if (!provider.Equals(StooqProvider, StringComparison.OrdinalIgnoreCase))
        {
            // Fail fast rather than silently falling back: a typo in the provider name would otherwise
            // look like the alternative provider quietly not being used.
            throw new InvalidOperationException(
                $"{StockQuoteProviderKey} is \"{provider}\", which is not a known quote provider. "
                + $"Use \"{StooqProvider}\" or \"{FinnhubProvider}\".");
        }

        services
            .AddHttpClient<IStockQuoteProvider, StooqClient>(StooqClient.HttpClientName, ConfigureStooqClient)
            .AddStandardResilienceHandler()
            .Configure(ConfigureStooqResilience);

        return services;
    }

    /// <summary>
    /// Bounds the buffered response. The base address is deliberately not set here: the client builds an
    /// absolute URL from <see cref="StooqOptions"/> itself, so there is one source for the endpoint.
    /// </summary>
    private static void ConfigureStooqClient(HttpClient client) =>
        client.MaxResponseContentBufferSize = StooqClient.MaxResponseBytes;

    private static void ConfigureFinnhubClient(HttpClient client) =>
        client.MaxResponseContentBufferSize = FinnhubClient.MaxResponseBytes;

    /// <summary>Same shape as the Stooq pipeline, driven by <see cref="FinnhubOptions.TimeoutSeconds"/>.</summary>
    private static void ConfigureFinnhubResilience(HttpStandardResilienceOptions resilience, IServiceProvider provider)
    {
        FinnhubOptions options = provider.GetRequiredService<IOptions<FinnhubOptions>>().Value;

        TimeSpan budget = TimeSpan.FromSeconds(options.TimeoutSeconds);
        TimeSpan perAttempt = budget / StooqClient.MaxAttemptsPerLookup;

        resilience.TotalRequestTimeout.Timeout = budget;
        resilience.AttemptTimeout.Timeout = perAttempt;
        resilience.Retry.MaxRetryAttempts = StooqClient.MaxAttemptsPerLookup - 1;
        resilience.Retry.Delay = TimeSpan.FromMilliseconds(StooqClient.RetryDelayMilliseconds);

        TimeSpan minimumSamplingDuration = perAttempt * 2;

        if (resilience.CircuitBreaker.SamplingDuration < minimumSamplingDuration)
        {
            resilience.CircuitBreaker.SamplingDuration = minimumSamplingDuration;
        }
    }

    private static void ConfigureStooqResilience(HttpStandardResilienceOptions resilience, IServiceProvider provider)
    {
        StooqOptions options = provider.GetRequiredService<IOptions<StooqOptions>>().Value;

        TimeSpan budget = TimeSpan.FromSeconds(options.TimeoutSeconds);
        TimeSpan perAttempt = budget / StooqClient.MaxAttemptsPerLookup;

        resilience.TotalRequestTimeout.Timeout = budget;
        resilience.AttemptTimeout.Timeout = perAttempt;
        resilience.Retry.MaxRetryAttempts = StooqClient.MaxAttemptsPerLookup - 1;
        resilience.Retry.Delay = TimeSpan.FromMilliseconds(StooqClient.RetryDelayMilliseconds);

        // The standard handler validates that the breaker samples at least two attempts; a generous
        // configured timeout would otherwise fail validation at startup rather than at the first call.
        TimeSpan minimumSamplingDuration = perAttempt * 2;

        if (resilience.CircuitBreaker.SamplingDuration < minimumSamplingDuration)
        {
            resilience.CircuitBreaker.SamplingDuration = minimumSamplingDuration;
        }
    }
}
