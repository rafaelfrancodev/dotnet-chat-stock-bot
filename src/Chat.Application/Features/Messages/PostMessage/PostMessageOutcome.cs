namespace Chat.Application.Features.Messages.PostMessage;

/// <summary>
/// What the handler did with one line of chat input. Returned instead of the created post because the
/// broadcast has already happened by the time the caller sees this: a hub that echoed a payload back to
/// the sender would render the message twice.
/// </summary>
public enum PostMessageOutcome
{
    /// <summary>Ordinary chat text: persisted, then broadcast to the room.</summary>
    Posted = 0,

    /// <summary>
    /// A <c>/stock=</c> command: handed to the bot over the broker. Deliberately <b>not</b> persisted —
    /// the command is a command, not a post, and only the bot's answer becomes a message.
    /// </summary>
    QuoteRequested = 1,
}
