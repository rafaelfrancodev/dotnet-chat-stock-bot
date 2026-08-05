namespace Chat.Application.Contracts.Realtime;

/// <summary>
/// A system condition worth interrupting a participant for, as opposed to a chat post.
/// </summary>
/// <remarks>
/// This exists because "the quote service is unreachable" is not an answer the bot can give in the
/// conversation — it is a statement about the system. Posting it only as a chat line would leave it to
/// scroll away among the messages, so it is delivered on its own channel and rendered as a banner.
/// </remarks>
/// <param name="Message">Text shown to the participant. Curated server-side; never echoes user input.</param>
/// <param name="Severity">How prominently the client should render it.</param>
public sealed record ChatAlert(string Message, ChatAlertSeverity Severity)
{
    /// <summary>
    /// Shown when Stooq answers with something unusable, times out, or cannot be reached at all —
    /// <c>StockQuoteOutcome.LookupFailed</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately says the quote service rather than "the bot": the bot is working, its upstream is
    /// not, and telling a participant to retry shortly is the only useful instruction. Distinct from an
    /// unknown ticker, which is a real answer and stays a chat message.
    /// </remarks>
    public static ChatAlert QuoteServiceUnavailable { get; } = new(
        "The stock quote service (Stooq) is not responding right now. Please try again in a couple of minutes.",
        ChatAlertSeverity.Error);
}

/// <summary>How prominently a <see cref="ChatAlert"/> should be rendered.</summary>
public enum ChatAlertSeverity
{
    /// <summary>Something the participant should notice but that did not stop their request.</summary>
    Warning = 1,

    /// <summary>The request could not be completed. Rendered as a red banner.</summary>
    Error = 2,
}
