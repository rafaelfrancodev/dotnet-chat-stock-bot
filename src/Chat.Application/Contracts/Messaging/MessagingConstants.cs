namespace Chat.Application.Contracts.Messaging;

/// <summary>
/// The single source of truth for the messaging topology shared by Chat.Web and Chat.Bot.
/// Nothing else in the solution may hard-code a queue name or a retry setting.
/// </summary>
/// <remarks>
/// MassTransit owns the exchange layout: publishing a message fans it out to an exchange named after
/// the message type, and each receive endpoint below gets a durable queue bound to the exchanges of the
/// messages its consumers handle. That is why no exchange or routing-key constants remain here —
/// inventing our own would fight the transport instead of configuring it.
/// </remarks>
public static class MessagingConstants
{
    /// <summary>
    /// Receive endpoint consumed by Chat.Bot: "please resolve this stock code". Competing consumers —
    /// any bot instance may serve a request.
    /// </summary>
    public const string StockQuoteRequestQueue = "stock-quote-requests";

    /// <summary>
    /// Receive endpoint consumed by Chat.Web: "here is the message to post in the room".
    /// </summary>
    public const string StockQuoteResponseQueue = "stock-quote-responses";

    /// <summary>
    /// Unacknowledged messages allowed per receive endpoint. Small on purpose: the work is I/O bound
    /// against a third party and cheap to retry.
    /// </summary>
    public const ushort PrefetchCount = 10;

    /// <summary>
    /// In-process delivery attempts before a message is moved aside. Chosen with
    /// <see cref="RetryIntervalSeconds"/> to ride out a brief Stooq blip without hammering it.
    /// </summary>
    public const int RetryLimit = 3;

    /// <summary>Delay between the retries described by <see cref="RetryLimit"/>.</summary>
    public const int RetryIntervalSeconds = 2;

    /// <summary>
    /// Suffix MassTransit appends when it gives up on a message. A poison message lands in
    /// <c>stock-quote-requests_error</c> rather than being requeued forever.
    /// </summary>
    public const string ErrorQueueSuffix = "_error";

    /// <summary>
    /// Suffix MassTransit appends for messages an endpoint received but had no consumer for.
    /// </summary>
    public const string SkippedQueueSuffix = "_skipped";
}
