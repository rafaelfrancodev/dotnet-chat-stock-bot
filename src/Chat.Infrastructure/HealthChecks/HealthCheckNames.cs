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
    /// <see cref="ReadyTag"/> tag, so it appears in <c>/health/ready</c> automatically. It reports
    /// readiness of the bus rather than reachability of the broker, and only reaches <c>Degraded</c>
    /// when a running bus loses the broker — see <c>RabbitMqHealthCheck</c> for the measurement.
    /// </summary>
    public const string MassTransitBus = "masstransit-bus";

    /// <summary>
    /// The third-party quote service the bot calls, whichever one <c>Stocks:Provider</c> selects.
    /// </summary>
    /// <remarks>
    /// Named for the role, not for a vendor, because the vendor is configuration. The check used to be
    /// called <c>stooq</c> and probed Stooq unconditionally, which reported on a service the bot was not
    /// calling once Finnhub became the default. The report's <c>description</c> names the provider that was
    /// actually probed, so <c>/health</c> answers "which provider is this process using?" on its own.
    /// </remarks>
    public const string StockQuoteProvider = "stock-quote-provider";

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
