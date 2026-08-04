using Chat.Domain.ChatRooms;

namespace Chat.Application.Abstractions.Persistence;

/// <summary>
/// Access to chat rooms. Phase 1 needs only existence checks and creation; the multiple-rooms bonus
/// adds listing and duplicate-name detection.
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
}
