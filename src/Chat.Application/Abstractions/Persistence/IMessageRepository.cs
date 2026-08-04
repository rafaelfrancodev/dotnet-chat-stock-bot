using Chat.Application.Contracts.Messages;
using Chat.Domain.ChatRooms;
using Chat.Domain.Messages;

namespace Chat.Application.Abstractions.Persistence;

/// <summary>
/// Write and read access to chat posts. The write side takes the aggregate; the read side returns
/// <see cref="MessageDto"/> only, so a <see cref="Message"/> never escapes persistence on a query path.
/// </summary>
public interface IMessageRepository
{
    /// <summary>
    /// Stages a new post for insertion. Nothing reaches the database until
    /// <see cref="IUnitOfWork.SaveChangesAsync"/> is called.
    /// </summary>
    /// <remarks>
    /// Synchronous by design: this only records an in-memory change, so returning a <c>Task</c> would
    /// imply I/O that does not happen. It is also what lets a handler test assert
    /// <c>repository.DidNotReceive().Add(...)</c> — the check that proves a <c>/stock=</c> command was
    /// never persisted, which is a hard challenge constraint.
    /// </remarks>
    /// <param name="message">The post to append.</param>
    void Add(Message message);

    /// <summary>
    /// Returns the most recent posts of a room, ordered oldest to newest so the caller can render them
    /// top to bottom without re-sorting.
    /// </summary>
    /// <remarks>
    /// Implementations must apply the ordering, the limit and the projection <b>in the database</b>
    /// (no tracking, no entity materialisation): the full history must never be loaded to serve this.
    /// </remarks>
    /// <param name="chatRoomId">Room whose history is wanted.</param>
    /// <param name="count">
    /// How many posts to return, newest-first before reversal. Defaults to
    /// <see cref="MessageConstants.LatestMessagesCount"/> (50), the challenge requirement.
    /// </param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<MessageDto>> GetLatestAsync(
        ChatRoomId chatRoomId,
        int count = MessageConstants.LatestMessagesCount,
        CancellationToken cancellationToken = default);
}
