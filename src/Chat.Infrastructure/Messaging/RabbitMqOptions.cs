namespace Chat.Infrastructure.Messaging;

/// <summary>
/// Connection settings for the RabbitMQ broker. Bound from the <c>RabbitMq</c> configuration section.
/// Credentials must come from user-secrets or environment variables, never from appsettings.json.
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "RabbitMq";

    /// <summary>Broker host name.</summary>
    public string HostName { get; init; } = "localhost";

    /// <summary>Broker AMQP port.</summary>
    public int Port { get; init; } = 5672;

    /// <summary>Virtual host to connect to.</summary>
    public string VirtualHost { get; init; } = "/";

    /// <summary>Broker user name.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>Broker password.</summary>
    public string Password { get; init; } = string.Empty;
}
