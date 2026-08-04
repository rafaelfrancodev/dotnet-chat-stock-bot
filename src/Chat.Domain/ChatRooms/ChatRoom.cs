using Chat.Domain.Common;

namespace Chat.Domain.ChatRooms;

/// <summary>
/// A room participants chat in, and its own aggregate root.
/// <para>
/// It deliberately holds <b>no collection of messages</b>. A post is its own aggregate
/// (<see cref="Messages.Message"/>) referencing the room by <see cref="ChatRoomId"/>, because the only
/// read the challenge asks for is "the last 50 posts of a room". A navigation collection here would
/// make that query load an unbounded history to throw almost all of it away, and would make the room
/// the consistency boundary of every post — two aggregates that never change together.
/// </para>
/// </summary>
public sealed class ChatRoom : AggregateRoot<ChatRoomId>
{
    private ChatRoom(ChatRoomId id, RoomName name, DateTimeOffset createdAtUtc)
        : base(id)
    {
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Materialisation constructor for the persistence layer only. EF Core bypasses the factory and
    /// writes every property (through their backing fields) immediately after calling it, so the
    /// null-forgiving assignment below is never observable. Owned value objects cannot be bound
    /// through a parameterised constructor, which is why this one exists.
    /// </summary>
    private ChatRoom()
        : base(default)
    {
        Name = null!;
    }

    /// <summary>The room's normalised display name. Unique across rooms (enforced by the database).</summary>
    public RoomName Name { get; private init; }

    /// <summary>
    /// When the room was created, normalised to UTC. Supplied by the caller (an
    /// <c>IDateTimeProvider</c>) rather than read from the clock here — the domain never reads time.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; private init; }

    /// <summary>Creates a room and raises <see cref="ChatRoomCreated"/>.</summary>
    /// <param name="name">The validated room name.</param>
    /// <param name="createdAtUtc">Creation time, supplied by the application clock. Must not be the default value.</param>
    public static Result<ChatRoom> Create(RoomName name, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (createdAtUtc == default)
        {
            return Result.Failure<ChatRoom>(Errors.MissingCreationTime);
        }

        // Normalised here so the room list cannot be ordered inconsistently by the caller's offset;
        // the instant is unchanged.
        ChatRoom room = new(ChatRoomId.New(), name, createdAtUtc.ToUniversalTime());
        room.Raise(new ChatRoomCreated(room.Id, room.Name, room.CreatedAtUtc));

        return Result.Success(room);
    }

    /// <summary>Expected failures of <see cref="Create"/>, with stable codes for tests and clients.</summary>
    public static class Errors
    {
        /// <summary>The creation time was the default value, which orders no room list correctly.</summary>
        public static readonly Error MissingCreationTime =
            Error.Validation("ChatRoom.MissingCreationTime", "A chat room must have a creation time.");
    }
}
