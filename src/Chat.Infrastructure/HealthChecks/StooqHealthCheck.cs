using System.Net;
using Chat.Infrastructure.Stocks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.HealthChecks;

/// <summary>
/// Verifies the Stooq quote service answers. Reports <see cref="HealthStatus.Degraded"/> rather than
/// unhealthy: Stooq is outside our control, and its outage must not make the bot look broken — the bot
/// answers "could not look that up right now" and keeps serving.
/// </summary>
/// <remarks>
/// Probes the service root, not a quote URL: a health endpoint must not spend the caller's rate budget
/// on a real ticker lookup.
/// </remarks>
internal sealed class StooqHealthCheck(HttpClient httpClient, IOptions<StooqOptions> options) : IHealthCheck
{
    private readonly StooqOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await httpClient
                .GetAsync(_options.BaseAddress, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            HttpStatusCode status = response.StatusCode;

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"Stooq responded {(int)status}.")
                : HealthCheckResult.Degraded($"Stooq responded {(int)status}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Degraded("Stooq is unreachable.", exception);
        }
    }
}
