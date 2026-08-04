using Chat.Domain.Common;

namespace Chat.Domain.Messages;

/// <summary>
/// The text of a chat post. Immutable, trimmed and bounded by
/// <see cref="MessageConstants.MaxContentLength"/>, so an invalid message cannot be represented.
/// </summary>
public sealed record MessageContent
{
    private MessageContent(string value) => Value = value;

    /// <summary>The trimmed text. Never empty.</summary>
    public string Value { get; }

    /// <summary>
    /// Validates and normalises <paramref name="value"/>: surrounding whitespace is removed and the
    /// result must be non-empty and at most <see cref="MessageConstants.MaxContentLength"/> characters.
    /// </summary>
    public static Result<MessageContent> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return Result.Failure<MessageContent>(Errors.Empty);
        }

        if (trimmed.Length > MessageConstants.MaxContentLength)
        {
            return Result.Failure<MessageContent>(Errors.TooLong);
        }

        return Result.Success(new MessageContent(trimmed));
    }

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>Expected failures of <see cref="Create"/>, with stable codes for tests and clients.</summary>
    public static class Errors
    {
        /// <summary>The text was null, empty or whitespace only.</summary>
        public static readonly Error Empty =
            Error.Validation("MessageContent.Empty", "A message cannot be empty.");

        /// <summary>The text exceeded the maximum length.</summary>
        public static readonly Error TooLong = Error.Validation(
            "MessageContent.TooLong",
            $"A message cannot exceed {MessageConstants.MaxContentLength} characters.");
    }
}
