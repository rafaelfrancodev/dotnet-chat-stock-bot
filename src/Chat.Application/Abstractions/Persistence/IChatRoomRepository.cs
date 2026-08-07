using Chat.Application.Contracts.Rooms;
using Chat.Domain.ChatRooms;

namespace Chat.Application.Abstractions.Persistence;

/// <summary>
/// Access to chat rooms: existence checks, creation, name lookup and the room list the picker renders.
/// </summary>
public interface IChatRoomRepository
{
    /// <summary>
    /// Stages a new room for insertion. Nothing reaches the database until
    /// <see cref="IUnitOfWork.SaveChangesAsync"/> is called. Synchronous for the same reason as
    /// <see cref="IMessageRepository.Add"/>.
    /// </summary>
    /// <param name="chatRoom">The room to create.</param>
    void Add(ChatRoom chatRoom);

    /// <summary>
    /// Whether a room exists. Handlers call this before posting or publishing so an unknown room fails
    /// as an expected <c>Result</c> instead of a foreign-key exception.
    /// </summary>
    /// <remarks>
    /// Implementations must answer with an existence query (<c>AnyAsync</c>), never by loading the
    /// aggregate — the caller only needs a boolean.
    /// </remarks>
    /// <param name="chatRoomId">Room to look for.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<bool> ExistsAsync(ChatRoomId chatRoomId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every room, ordered by name, as the projection the room picker renders.
    /// </summary>
    /// <remarks>
    /// Unpaged deliberately, and it is the one read in the system that is: a room directory is
    /// operator-scale, not user-scale — rooms are created by hand and counted in dozens, unlike messages,
    /// which is why <see cref="IMessageRepository"/> caps its read at 50 and this does not. Ordering by
    /// name rather than creation time keeps the list stable as rooms are added, so a participant's chosen
    /// room does not move under the cursor.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<ChatRoomDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a room by its name, or <see langword="null"/> when no room carries it. The chat page uses
    /// this to resolve the room it opens on, and room creation uses it to reject a duplicate name.
    /// </summary>
    /// <remarks>
    /// Returns a projection, not the aggregate: this is a read path, and the caller renders a name and
    /// an identifier. Implementations must let the unique index on the name serve the lookup.
    /// </remarks>
    /// <param name="name">Room name to look for. Already normalised by the value object.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<ChatRoomDto?> FindByNameAsync(RoomName name, CancellationToken cancellationToken = default);
}
