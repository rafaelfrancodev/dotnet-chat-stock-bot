using Chat.Domain.Common;

namespace Chat.Domain.StockCommands;

/// <summary>
/// What a line typed into the chat box actually is, as decided by <see cref="ChatCommandParser.Parse"/>.
/// <para>
/// A closed hierarchy: the constructor is private, so the four nested records below are the complete
/// set of cases and callers can branch with a <c>switch</c> expression on the type — no string
/// comparisons, no casts. This classification is the gate for the challenge's hard rule that a
/// <c>/stock=</c> command is never stored as a post, so a new case must never be added silently.
/// </para>
/// </summary>
public abstract record ParsedChatInput
{
    private ParsedChatInput()
    {
    }

    /// <summary>
    /// Ordinary chat text: the only case that may be persisted and broadcast to the room.
    /// </summary>
    /// <param name="Text">
    /// The text with surrounding whitespace removed. Untrusted user input: validate it through
    /// <c>MessageContent.Create</c> and render it as text, never as HTML.
    /// </param>
    public sealed record PlainMessage(string Text) : ParsedChatInput;

    /// <summary>
    /// A well-formed <c>/stock=code</c> command. It is a command, not a post: it must be dispatched
    /// to the bot and must <b>never</b> be written to the messages table.
    /// </summary>
    /// <param name="Code">The validated ticker, safe to interpolate into the Stooq URL.</param>
    public sealed record StockQuote(StockCode Code) : ParsedChatInput;

    /// <summary>
    /// A slash command the chat does not implement (for example <c>/help</c>). Nothing is persisted
    /// or published; the caller is told privately.
    /// </summary>
    /// <param name="CommandName">
    /// The lower-cased name as typed, with no leading slash. Untrusted user input: echo it back as
    /// text only.
    /// </param>
    public sealed record UnknownCommand(string CommandName) : ParsedChatInput;

    /// <summary>
    /// A recognised command whose argument cannot be used — for example <c>/stock=</c> or
    /// <c>/stock=a&amp;b</c>. Nothing is persisted or published.
    /// </summary>
    /// <param name="Error">
    /// Why the command was rejected. Comes from <see cref="StockCode.Errors"/> whenever the ticker
    /// itself is at fault, so the parser never restates <see cref="StockCode"/>'s rules.
    /// </param>
    public sealed record Invalid(Error Error) : ParsedChatInput;
}
