using Chat.Domain.Common;

namespace Chat.Domain.Messages;

/// <summary>
/// The owner of a post: either a registered participant (identified by the authentication user id)
/// or the well-known <see cref="Bot"/>, which owns every stock quote answer.
/// </summary>
public sealed record MessageAuthor
{
    /// <summary>Stable identifier of the bot author. It is not an Identity user.</summary>
    public const string BotUserId = "system:bot";

    /// <summary>Display name shown for bot posts in the chat window.</summary>
    public const string BotDisplayName = "Bot";

    private MessageAuthor(string userId, string displayName)
    {
        UserId = userId;
        DisplayName = displayName;
    }

    /// <summary>The well-known bot author, post owner of every stock quote answer.</summary>
    public static MessageAuthor Bot { get; } = new(BotUserId, BotDisplayName);

    /// <summary>Authentication user id, taken from the caller's claims — never from a client payload.</summary>
    public string UserId { get; }

    /// <summary>Human-readable name rendered in the chat window.</summary>
    public string DisplayName { get; }

    /// <summary>True when this post was written by the bot rather than by a participant.</summary>
    public bool IsBot => UserId == BotUserId;

    /// <summary>Validates and trims both parts; neither may be empty or whitespace only.</summary>
    public static Result<MessageAuthor> Create(string? userId, string? displayName)
    {
        string trimmedUserId = userId?.Trim() ?? string.Empty;
        if (trimmedUserId.Length == 0)
        {
            return Result.Failure<MessageAuthor>(Errors.EmptyUserId);
        }

        string trimmedDisplayName = displayName?.Trim() ?? string.Empty;
        if (trimmedDisplayName.Length == 0)
        {
            return Result.Failure<MessageAuthor>(Errors.EmptyDisplayName);
        }

        return Result.Success(new MessageAuthor(trimmedUserId, trimmedDisplayName));
    }

    /// <summary>Expected failures of <see cref="Create"/>, with stable codes for tests and clients.</summary>
    public static class Errors
    {
        /// <summary>The user id was null, empty or whitespace only.</summary>
        public static readonly Error EmptyUserId =
            Error.Validation("MessageAuthor.EmptyUserId", "A message author must have a user id.");

        /// <summary>The display name was null, empty or whitespace only.</summary>
        public static readonly Error EmptyDisplayName =
            Error.Validation("MessageAuthor.EmptyDisplayName", "A message author must have a display name.");
    }
}
