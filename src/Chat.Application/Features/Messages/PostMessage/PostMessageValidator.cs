using Chat.Domain.ChatRooms;
using Chat.Domain.Messages;
using FluentValidation;

namespace Chat.Application.Features.Messages.PostMessage;

/// <summary>
/// Rejects a post that cannot possibly be handled before it reaches the parser or the database. Runs in
/// <c>ValidationBehavior</c>, so a violation comes back as a failed <c>Result</c>, never an exception.
/// </summary>
/// <remarks>
/// The author rules are not ceremony. Only the plain-message branch builds a <c>MessageAuthor</c>, so an
/// empty identity would otherwise slip through the <c>/stock=</c> branch and reach the broker inside a
/// quote request. Checking it once, here, covers both branches.
/// <para>
/// The length rule bounds work the same way <c>GetLatestMessagesValidator</c> bounds reads: an unbounded
/// line would be parsed, trimmed and allocated before any value object could refuse it.
/// </para>
/// </remarks>
internal sealed class PostMessageValidator : AbstractValidator<PostMessageCommand>
{
    /// <summary>
    /// Longest line accepted, measured after trimming — the same normalisation
    /// <see cref="MessageContent.Create"/> applies, so this bound rejects exactly what the value object
    /// would have rejected and nothing more.
    /// </summary>
    internal const int MaxRawInputLength = MessageConstants.MaxContentLength;

    public PostMessageValidator()
    {
        RuleFor(command => command.ChatRoomId)
            .NotEqual(default(ChatRoomId))
            .WithMessage("A chat room must be specified.");

        RuleFor(command => command.RawInput)
            .NotEmpty()
            .WithMessage("A message cannot be empty.")
            .Must(rawInput => rawInput is null || rawInput.AsSpan().Trim().Length <= MaxRawInputLength)
            .WithMessage($"A message cannot exceed {MaxRawInputLength} characters.");

        // Both come from the caller's claims, so an empty value is a bug at the edge, not user input.
        RuleFor(command => command.AuthorUserId)
            .NotEmpty()
            .WithMessage("The author must be identified.");

        RuleFor(command => command.AuthorDisplayName)
            .NotEmpty()
            .WithMessage("The author must have a display name.");
    }
}
