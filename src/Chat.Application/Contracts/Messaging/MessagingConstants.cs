namespace Chat.Application.Contracts.Messaging;

/// <summary>
/// The single source of truth for the RabbitMQ topology shared by Chat.Web and Chat.Bot.
/// Nothing else in the solution may hard-code an exchange, queue or routing-key string.
/// </summary>
public static class MessagingConstants
{
    /// <summary>Durable direct exchange carrying the whole stock-quote conversation.</summary>
    public const string StockExchange = "chat.stock";

    /// <summary>Dead-letter exchange for messages the consumers reject as unprocessable.</summary>
    public const string StockDeadLetterExchange = "chat.stock.dlx";

    public static class RoutingKeys
    {
        /// <summary>Chat.Web -> Chat.Bot: "please resolve this stock code".</summary>
        public const string StockQuoteRequest = "stock.quote.request";

        /// <summary>Chat.Bot -> Chat.Web: "here is the message to post in the room".</summary>
        public const string StockQuoteResponse = "stock.quote.response";
    }

    public static class Queues
    {
        /// <summary>Consumed by Chat.Bot. Competing consumers: any bot instance may serve a request.</summary>
        public const string StockQuoteRequests = "stock.quote.requests";

        /// <summary>Consumed by Chat.Web, which turns the payload into a bot post and a SignalR broadcast.</summary>
        public const string StockQuoteResponses = "stock.quote.responses";

        public const string StockQuoteRequestsDeadLetter = "stock.quote.requests.dlq";

        public const string StockQuoteResponsesDeadLetter = "stock.quote.responses.dlq";
    }

    /// <summary>Unacknowledged messages allowed per consumer channel. Small on purpose: work is I/O bound and cheap to retry.</summary>
    public const ushort PrefetchCount = 10;

    /// <summary>MIME type stamped on every published message body.</summary>
    public const string ContentType = "application/json";
}
