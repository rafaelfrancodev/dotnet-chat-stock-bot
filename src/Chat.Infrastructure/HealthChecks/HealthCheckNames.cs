namespace Chat.Infrastructure.HealthChecks;

/// <summary>
/// Well-known health check names and tags, shared by both hosts so the JSON payloads and the
/// <c>/health/ready</c> filter stay consistent between Chat.Web and Chat.Bot.
/// </summary>
public static class HealthCheckNames
{
    /// <summary>The relational store behind chat history and Identity.</summary>
    public const string SqlServer = "sql-server";

    /// <summary>The message broker carrying stock quote requests and responses.</summary>
    public const string RabbitMq = "rabbitmq";

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
