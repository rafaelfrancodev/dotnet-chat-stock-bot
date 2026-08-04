using System.Text;
using Chat.Domain.Common;

namespace Chat.Domain.ChatRooms;

/// <summary>
/// The display name of a chat room. Immutable and normalised: surrounding whitespace is removed and
/// every internal run of whitespace becomes a single space, so <c>"General   Chat"</c> and
/// <c>"General Chat"</c> are the same name and cannot both exist.
/// <para>
/// Normalisation happens before the length check, exactly as <see cref="Messages.MessageContent"/>
/// trims before measuring: the limit applies to what is actually stored and displayed, so a name that
/// only exceeded it because of duplicated spaces is accepted rather than rejected for invisible input.
/// </para>
/// </summary>
public sealed record RoomName
{
    /// <summary>Upper bound on a room name, measured after normalisation.</summary>
    public const int MaxLength = 60;

    private RoomName(string value) => Value = value;

    /// <summary>The normalised name. Never empty, never padded, never doubly spaced.</summary>
    public string Value { get; }

    /// <summary>
    /// Validates and normalises <paramref name="value"/>: it is trimmed, internal whitespace runs are
    /// collapsed to a single space, and the result must be non-empty and at most
    /// <see cref="MaxLength"/> characters.
    /// </summary>
    public static Result<RoomName> Create(string? value)
    {
        string normalised = CollapseWhitespace(value);

        if (normalised.Length == 0)
        {
            return Result.Failure<RoomName>(Errors.Empty);
        }

        if (normalised.Length > MaxLength)
        {
            return Result.Failure<RoomName>(Errors.TooLong);
        }

        return Result.Success(new RoomName(normalised));
    }

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>
    /// Trims and replaces every run of whitespace with a single space. All Unicode whitespace counts,
    /// so tabs and newlines cannot smuggle layout into a room name. Nothing is allocated on the common
    /// path: an already-normalised name is returned as-is and only a name that needs rewriting is
    /// rebuilt.
    /// </summary>
    private static string CollapseWhitespace(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (!NeedsCollapsing(trimmed))
        {
            return trimmed;
        }

        StringBuilder builder = new(trimmed.Length);
        bool previousWasWhitespace = false;

        foreach (char character in trimmed)
        {
            if (char.IsWhiteSpace(character))
            {
                previousWasWhitespace = true;
                continue;
            }

            if (previousWasWhitespace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString();
    }

    /// <summary>
    /// True when the (already trimmed) text contains a whitespace character other than a single space,
    /// or two adjacent whitespace characters.
    /// </summary>
    private static bool NeedsCollapsing(ReadOnlySpan<char> trimmed)
    {
        for (int index = 0; index < trimmed.Length; index++)
        {
            if (!char.IsWhiteSpace(trimmed[index]))
            {
                continue;
            }

            if (trimmed[index] != ' ')
            {
                return true;
            }

            if (index + 1 < trimmed.Length && char.IsWhiteSpace(trimmed[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Expected failures of <see cref="Create"/>, with stable codes for tests and clients.</summary>
    public static class Errors
    {
        /// <summary>The name was null, empty or whitespace only.</summary>
        public static readonly Error Empty =
            Error.Validation("RoomName.Empty", "A chat room name cannot be empty.");

        /// <summary>The normalised name exceeded the maximum length.</summary>
        public static readonly Error TooLong = Error.Validation(
            "RoomName.TooLong",
            $"A chat room name cannot exceed {MaxLength} characters.");
    }
}
