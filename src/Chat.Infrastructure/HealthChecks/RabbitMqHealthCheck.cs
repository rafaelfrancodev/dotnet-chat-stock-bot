using Chat.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Chat.Infrastructure.HealthChecks;

/// <summary>
/// Verifies the broker is reachable and the configured credentials and virtual host are accepted.
/// </summary>
/// <remarks>
/// Opens a short-lived connection per probe. Once task 1.10 introduces the shared singleton
/// <see cref="IConnection"/>, this check should resolve that connection and report
/// <c>IsOpen</c> instead, so probing costs nothing.
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
