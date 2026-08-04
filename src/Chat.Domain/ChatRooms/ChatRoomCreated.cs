using Chat.Domain.Common;

namespace Chat.Domain.ChatRooms;

/// <summary>
/// A chat room was created. Raised by <see cref="ChatRoom.Create"/>, dispatched by the Application
/// layer <b>after</b> the unit of work commits, so a failed save can never announce a room that does
/// not exist.
/// </summary>
/// <param name="ChatRoomId">Identity of the new room — the SignalR group name participants join.</param>
/// <param name="Name">The normalised room name to render in the room list.</param>
/// <param name="OccurredAtUtc">When the room was created, in UTC. Same instant as <see cref="ChatRoom.CreatedAtUtc"/>.</param>
public sealed record ChatRoomCreated(
    ChatRoomId ChatRoomId,
    RoomName Name,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
