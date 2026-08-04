using Chat.Domain.ChatRooms;
using Chat.Domain.Messages;
using FluentValidation;

namespace Chat.Application.Features.Messages.GetLatestMessages;

/// <summary>
/// Bounds the query before it reaches the database. Runs in <c>ValidationBehavior</c>, so a violation
/// comes back as a failed <c>Result</c> rather than an exception.
/// </summary>
/// <remarks>
/// The upper bound is the point of this validator. <see cref="GetLatestMessagesQuery.Count"/> reaches
/// SQL as <c>TOP(n)</c>, so a caller-supplied count is a lever on how much the database reads and how
/// much every chat window downloads — the resource consumption the challenge explicitly warns about.
/// Capping it at <see cref="MessageConstants.LatestMessagesCount"/> also makes "show only the last 50"
/// enforced rather than merely defaulted: no client can ask for the 51st message.
/// </remarks>
internal sealed class GetLatestMessagesValidator : AbstractValidator<GetLatestMessagesQuery>
{
    /// <summary>Fewest posts worth a round trip. Zero or less identifies a caller bug, not an empty room.</summary>
    internal const int MinimumCount = 1;

    public GetLatestMessagesValidator()
    {
        RuleFor(query => query.ChatRoomId)
            .NotEqual(default(ChatRoomId))
            .WithMessage("A chat room must be specified.");

        RuleFor(query => query.Count)
            .InclusiveBetween(MinimumCount, MessageConstants.LatestMessagesCount)
            .WithMessage(
                $"The number of messages must be between {MinimumCount} and {MessageConstants.LatestMessagesCount}.");
    }
}
