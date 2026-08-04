namespace Chat.Infrastructure.HealthChecks;

/// <summary>
/// Well-known health check names and tags, shared by both hosts so the JSON payloads and the
/// <c>/health/ready</c> filter stay consistent between Chat.Web and Chat.Bot.
/// </summary>
public static class HealthCheckNames
{
    /// <summary>The relational store behind chat history and Identity.</summary>
    public const string SqlServer = "sql-server";

    /// <summary>Direct broker connectivity probe. See <c>RabbitMqHealthCheck</c>.</summary>
    public const string RabbitMq = "rabbitmq";

    /// <summary>
    /// Bus lifecycle state. Registered and named by MassTransit itself when the bus is added, not by
    /// us — declared here only so the name has one documented home. It already carries the
    /// <see cref="ReadyTag"/> tag, so it appears in <c>/health/ready</c> automatically.
    /// Note it reports readiness of the bus, not reachability of the broker.
    /// </summary>
    public const string MassTransitBus = "masstransit-bus";

    /// <summary>The third-party quote provider the bot calls.</summary>
    public const string Stooq = "stooq";

    /// <summary>
    /// Dependencies a host must reach before it can do useful work. Surfaced by <c>/health/ready</c>.
    /// </summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// Services outside our control. Reported by <c>/health</c> but deliberately excluded from
    /// <c>/health/ready</c> so a third-party outage never marks the process itself as unready.
    /// </summary>
    public const string ExternalTag = "external";
}
