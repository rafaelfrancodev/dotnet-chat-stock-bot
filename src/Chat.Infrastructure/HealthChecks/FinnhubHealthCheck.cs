using System.Net;
using Chat.Infrastructure.Stocks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.HealthChecks;

/// <summary>
/// Verifies the Finnhub quote service answers. Reports <see cref="HealthStatus.Degraded"/> rather than
/// unhealthy: Finnhub is outside our control, and its outage must not make the bot look broken — the bot
/// answers "could not look that up right now" and keeps serving.
/// </summary>
/// <remarks>
/// Probes the service root, not a quote URL: a health endpoint must not spend the caller's rate budget on
/// a real ticker lookup, and on a free key that budget is the scarce resource.
/// <para>
/// A missing API key is reported here rather than being left to show up as a puzzling wrong answer in the
/// chat room. It is the single most likely thing to be unset on a fresh clone, it degrades every lookup,
/// and nothing else in the process complains about it at startup.
/// </para>
/// </remarks>
internal sealed class FinnhubHealthCheck(HttpClient httpClient, IOptions<FinnhubOptions> options) : IHealthCheck
{
    private readonly FinnhubOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return HealthCheckResult.Degraded(
                "Finnhub is the configured quote provider, but Finnhub:ApiKey is not set, so every lookup "
                + "answers with a friendly failure instead of a price. Set it with \"dotnet user-secrets "
                + "set \"Finnhub:ApiKey\" \"<key>\" --project src/Chat.Bot\" when running from the SDK, or "
                + "the Finnhub__ApiKey environment variable in a container. See README -> Configuration.");
        }

        try
        {
            using HttpResponseMessage response = await httpClient
                .GetAsync(_options.BaseAddress, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            HttpStatusCode status = response.StatusCode;

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"Finnhub responded {(int)status}.")
                : HealthCheckResult.Degraded($"Finnhub responded {(int)status}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Degraded("Finnhub is unreachable.", exception);
        }
    }
}
