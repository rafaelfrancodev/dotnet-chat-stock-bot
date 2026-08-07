using Chat.Application.Abstractions.Messaging;
using Chat.Application.Contracts.Rooms;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;

namespace Chat.Application.Features.Rooms.CreateRoom;

/// <summary>
/// Creates a chat room and returns it, so the caller can join it immediately.
/// </summary>
/// <remarks>
/// It carries a name and nothing else. There is no creator, no visibility and no membership: the challenge
/// asks for more than one chatroom, not for room ownership, and inventing an owner would add a
/// relationship every read would then have to honour.
/// </remarks>
/// <param name="Name">
/// The requested name, exactly as typed. Untrusted and unnormalised — <see cref="RoomName"/> owns both,
/// and it is the value object's normalisation that makes "General" and "General   " the same room rather
/// than two.
/// </param>
public sealed record CreateRoomCommand(string Name) : ICommand<ChatRoomDto>
{
    /// <summary>Expected failures of this use case, with stable codes for tests and clients.</summary>
    public static class Errors
    {
        /// <summary>
        /// A room already carries this name. An expected outcome rather than a conflict exception: two
        /// participants naming a room "Trading" is ordinary, and the second one needs a sentence they can
        /// act on, not a stack trace.
        /// </summary>
        public static readonly Error NameTaken = Error.Validation(
            "ChatRoom.NameTaken",
            "A chat room with that name already exists. Pick a different name, or join the existing room.");
    }
}
