using Chat.Domain.Common;

namespace Chat.Domain.StockCommands;

/// <summary>
/// Decides whether a chat line is a post or a command. This runs on every message sent by every
/// participant, so it is a total function over <see cref="string"/>: it allocates almost nothing on
/// the plain-message path and never throws, whatever the user types.
/// </summary>
public static class ChatCommandParser
{
    /// <summary>Marks a line as a command instead of chat text.</summary>
    public const char CommandPrefix = '/';

    /// <summary>Separates a command name from its argument, as in <c>/stock=aapl.us</c>.</summary>
    public const char ArgumentSeparator = '=';

    /// <summary>The only command the chat implements. Matched with case-insensitive ordinal comparison.</summary>
    public const string StockCommandName = "stock";

    /// <summary>
    /// Classifies <paramref name="rawInput"/>. Surrounding whitespace is tolerated everywhere, and
    /// <c>null</c>, empty, control characters and very long strings are all valid input that simply
    /// produce a case of <see cref="ParsedChatInput"/>.
    /// </summary>
    /// <param name="rawInput">Exactly what the participant typed.</param>
    /// <returns>
    /// <see cref="ParsedChatInput.StockQuote"/> for <c>/stock=aapl.us</c>,
    /// <see cref="ParsedChatInput.Invalid"/> for a stock command with an unusable ticker,
    /// <see cref="ParsedChatInput.UnknownCommand"/> for any other slash command, and
    /// <see cref="ParsedChatInput.PlainMessage"/> for everything else.
    /// </returns>
    public static ParsedChatInput Parse(string? rawInput)
    {
        string trimmed = rawInput?.Trim() ?? string.Empty;

        if (trimmed.Length == 0 || trimmed[0] != CommandPrefix)
        {
            return new ParsedChatInput.PlainMessage(trimmed);
        }

        // A command is "/<name>" or "/<name>=<argument>"; the prefix is dropped before splitting so
        // that a '=' inside the argument (for example "/stock==") stays part of the argument.
        ReadOnlySpan<char> command = trimmed.AsSpan(start: 1);
        int separatorIndex = command.IndexOf(ArgumentSeparator);
        ReadOnlySpan<char> name = (separatorIndex < 0 ? command : command[..separatorIndex]).Trim();

        if (name.IsEmpty)
        {
            return new ParsedChatInput.Invalid(Errors.MissingCommandName);
        }

        // Ordinal ignore-case, never culture-sensitive: under tr-TR a culture-aware comparison would
        // change which command names match.
        bool isStockCommand = separatorIndex >= 0
            && name.Equals(StockCommandName, StringComparison.OrdinalIgnoreCase);

        if (!isStockCommand)
        {
            return new ParsedChatInput.UnknownCommand(name.ToString().ToLowerInvariant());
        }

        // StockCode owns every rule about tickers — trimming, casing, length and the character
        // allow-list — so an unusable argument is reported with its error, not a duplicated one.
        Result<StockCode> stockCode = StockCode.Create(command[(separatorIndex + 1)..].ToString());

        return stockCode.IsSuccess
            ? new ParsedChatInput.StockQuote(stockCode.Value)
            : new ParsedChatInput.Invalid(stockCode.Error);
    }

    /// <summary>Expected failures of <see cref="Parse"/>, with stable codes for tests and clients.</summary>
    public static class Errors
    {
        /// <summary>The line started with a slash but named no command, as in <c>/</c> or <c>/=</c>.</summary>
        public static readonly Error MissingCommandName = Error.Validation(
            "ChatCommand.MissingCommandName",
            $"A command must have a name, for example {CommandPrefix}{StockCommandName}{ArgumentSeparator}aapl.us.");
    }
}
