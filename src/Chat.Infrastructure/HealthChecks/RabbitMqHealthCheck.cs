using Chat.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Chat.Infrastructure.HealthChecks;

/// <summary>
/// Verifies the broker is reachable and the configured credentials and virtual host are accepted.
/// </summary>
/// <remarks>
/// This probe exists *alongside* MassTransit's own <c>masstransit-bus</c> check, and task 1.10 settled
/// why by measuring both with a real receive endpoint running:
/// <list type="bullet">
/// <item>broker already down at startup — the bus reports <c>Unhealthy</c> ("Not ready: not started");</item>
/// <item>broker stopped after a healthy start — the bus reports <c>Degraded</c> ("Degraded Endpoints:
/// stock-quote-requests") for 60 s+, and <c>Degraded</c> maps to HTTP 200, so <c>/health/ready</c> keeps
/// answering "ready" while no quote can flow.</item>
/// </list>
/// The second case is the one that matters for an outage in production, so this check is kept: it goes
/// <c>Unhealthy</c> immediately and correctly drives <c>/health/ready</c> to 503. The cost is one
/// short-lived connection per probe, which is what buys an accurate readiness signal.
/// </remarks>
internal sealed class RabbitMqHealthCheck(IOptions<RabbitMqOptions> options) : IHealthCheck
{
    private readonly RabbitMqOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ConnectionFactory factory = new()
            {
                HostName = _options.HostName,
                Port = _options.Port,
                VirtualHost = _options.VirtualHost,
                UserName = _options.UserName,
                Password = _options.Password,
            };

            await using IConnection connection =
                await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            return connection.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ connection established.")
                : HealthCheckResult.Unhealthy("RabbitMQ connection closed immediately after opening.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is unreachable.", exception);
        }
    }
}
