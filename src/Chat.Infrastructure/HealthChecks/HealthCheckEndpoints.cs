using System.Net.Mime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chat.Infrastructure.HealthChecks;

/// <summary>
/// Maps the health endpoints both hosts expose. Defined once here so Chat.Web and Chat.Bot cannot
/// drift apart in route names or payload shape.
/// </summary>
public static class HealthCheckEndpoints
{
    /// <summary>Every registered dependency, including the ones that never gate readiness.</summary>
    public const string HealthRoute = "/health";

    /// <summary>Dependencies the process needs before it can do useful work.</summary>
    public const string ReadyRoute = "/health/ready";

    /// <summary>The process itself. Runs no dependency probe, so a broker outage cannot trigger a restart.</summary>
    public const string LiveRoute = "/health/live";

    /// <summary>Maps <c>/health</c>, <c>/health/ready</c> and <c>/health/live</c>.</summary>
    public static IEndpointRouteBuilder MapChatHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks(HealthRoute, JsonOptions(_ => true))
            .AllowAnonymous();

        endpoints.MapHealthChecks(ReadyRoute, JsonOptions(static registration =>
                registration.Tags.Contains(HealthCheckNames.ReadyTag)))
            .AllowAnonymous();

        // Predicate excluding every check: liveness answers "is this process running", nothing more.
        endpoints.MapHealthChecks(LiveRoute, new HealthCheckOptions { Predicate = static _ => false })
            .AllowAnonymous();

        return endpoints;
    }

    private static HealthCheckOptions JsonOptions(Func<HealthCheckRegistration, bool> predicate) =>
        new()
        {
            Predicate = predicate,
            ResponseWriter = static (context, report) =>
            {
                context.Response.ContentType = MediaTypeNames.Application.Json;
                return context.Response.WriteAsync(HealthReportSerializer.ToJson(report));
            },
        };
}
