using Chat.Domain.ChatRooms;
using FluentValidation;

namespace Chat.Application.Features.Rooms.CreateRoom;

/// <summary>
/// Rejects a room name that no normalisation could rescue, before the use case runs.
/// </summary>
/// <remarks>
/// This validator deliberately does <b>not</b> restate <see cref="RoomName"/>'s rules. It checks only that
/// there is some non-whitespace text to normalise, because a request carrying nothing at all is a caller
/// bug worth stopping at the pipeline. Length is left to the value object: <see cref="RoomName"/> collapses
/// whitespace <i>before</i> measuring, so a name that only exceeds the limit through duplicated spaces is
/// accepted — a rule this layer cannot reproduce without duplicating the normaliser, and duplicating it is
/// how the two would drift apart.
/// </remarks>
internal sealed class CreateRoomValidator : AbstractValidator<CreateRoomCommand>
{
    public CreateRoomValidator() =>
        RuleFor(command => command.Name)
            .NotEmpty()
            .WithMessage("A chat room name is required.");
}
