using Chat.Infrastructure.Messaging;
using Chat.Infrastructure.Persistence;
using Chat.Infrastructure.Stocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.HealthChecks;

/// <summary>
/// Registers a dependency probe per infrastructure concern. Each host opts in to only the dependencies
/// it actually has: Chat.Web takes the database and the broker, Chat.Bot takes the broker and Stooq.
/// </summary>
public static class HealthCheckBuilderExtensions
{
    /// <summary>Probes the chat database with <c>SELECT 1</c>. Gates readiness.</summary>
    public static IHealthChecksBuilder AddChatDatabase(
        this IHealthChecksBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        string? connectionString = configuration.GetConnectionString(PersistenceConstants.ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Reported rather than thrown: a host can register this check without AddPersistence (which
            // does fail fast), and a probe endpoint that explains the gap beats one that cannot answer.
            return builder.AddCheck(
                HealthCheckNames.SqlServer,
                new NotConfiguredHealthCheck(
                    $"ConnectionStrings:{PersistenceConstants.ConnectionStringName} is not set. " +
                    "See README -> Configuration."),
                HealthStatus.Unhealthy,
                [HealthCheckNames.ReadyTag]);
        }

        return builder.AddCheck(
            HealthCheckNames.SqlServer,
            new SqlServerHealthCheck(connectionString),
            HealthStatus.Unhealthy,
            [HealthCheckNames.ReadyTag]);
    }

    /// <summary>
    /// Probes the RabbitMQ broker directly. Gates readiness — without it no stock quote can flow.
    /// </summary>
    /// <remarks>
    /// Complements, rather than duplicates, MassTransit's <c>masstransit-bus</c> check: that one
    /// reports bus lifecycle state and stays healthy while the broker is down until receive endpoints
    /// exist. See <see cref="RabbitMqHealthCheck"/> for the measurement and the removal trigger.
    /// </remarks>
    public static IHealthChecksBuilder AddChatBroker(
        this IHealthChecksBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services
            .AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName));

        return builder.AddCheck<RabbitMqHealthCheck>(
            HealthCheckNames.RabbitMq,
            HealthStatus.Unhealthy,
            [HealthCheckNames.ReadyTag]);
    }

    /// <summary>
    /// Probes Stooq. Tagged <see cref="HealthCheckNames.ExternalTag"/> and excluded from readiness:
    /// the bot stays ready during a Stooq outage and answers with a friendly failure instead.
    /// </summary>
    public static IHealthChecksBuilder AddStooq(
        this IHealthChecksBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services
            .AddOptions<StooqOptions>()
            .Bind(configuration.GetSection(StooqOptions.SectionName));

        builder.Services
            .AddHttpClient<StooqHealthCheck>(static (provider, client) =>
                client.Timeout = TimeSpan.FromSeconds(
                    provider.GetRequiredService<IOptions<StooqOptions>>().Value.TimeoutSeconds));

        return builder.AddCheck<StooqHealthCheck>(
            HealthCheckNames.Stooq,
            HealthStatus.Degraded,
            [HealthCheckNames.ExternalTag]);
    }

    /// <summary>Reports a configuration gap as an unhealthy dependency with an actionable message.</summary>
    private sealed class NotConfiguredHealthCheck(string reason) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Unhealthy(reason));
    }
}
