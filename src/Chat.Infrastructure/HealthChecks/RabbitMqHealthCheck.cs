using Chat.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Chat.Infrastructure.HealthChecks;

/// <summary>
/// Verifies the broker is reachable and the configured credentials and virtual host are accepted.
/// </summary>
/// <remarks>
/// This probe exists *alongside* MassTransit's own <c>masstransit-bus</c> check because that check
/// reports bus lifecycle state, not connectivity. Measured with the broker stopped: the bus check
/// stayed <c>Healthy</c> indefinitely and logged no connection attempt, because a bus with no receive
/// endpoints registered never opens one. Until consumers exist it is not a dependency probe at all.
/// <para>
/// Task 1.10 should re-measure once the receive endpoints are registered: if a stopped broker then
/// turns <c>masstransit-bus</c> unhealthy, delete this class and its registration, because the bus
/// check costs nothing while this one opens a short-lived connection per probe.
/// </para>
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
