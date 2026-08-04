using Chat.Application.Abstractions.Persistence;
using Chat.Application.Contracts.Messages;
using Chat.Domain.ChatRooms;
using Chat.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IMessageRepository"/>: aggregates go in, projections come out.
/// </summary>
/// <param name="context">The chat database context, scoped to the current unit of work.</param>
internal sealed class MessageRepository(ChatDbContext context) : IMessageRepository
{
    /// <inheritdoc/>
    public void Add(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Staged only. Nothing is written until IUnitOfWork.SaveChangesAsync commits.
        context.Messages.Add(message);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MessageDto>> GetLatestAsync(
        ChatRoomId chatRoomId,
        int count = MessageConstants.LatestMessagesCount,
        CancellationToken cancellationToken = default)
    {
        List<LatestMessageRow> newestFirst = await LatestMessagesQuery(context, chatRoomId, count)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        MessageDto[] oldestFirst = new MessageDto[newestFirst.Count];

        // One pass reverses the rows and unwraps the value objects. The database already applied the
        // filter, the ordering and the limit, so this touches at most `count` rows.
        for (int index = 0; index < oldestFirst.Length; index++)
        {
            LatestMessageRow row = newestFirst[newestFirst.Count - 1 - index];

            oldestFirst[index] = new MessageDto(
                row.Id.Value,
                row.AuthorDisplayName,
                row.Content.Value,
                row.PostedAtUtc,
                row.Origin);
        }

        return oldestFirst;
    }

    /// <summary>
    /// The "last N messages of a room" query, newest first, as a single indexed statement:
    /// no tracking, no entity materialisation and only the five columns the chat window renders.
    /// </summary>
    /// <remarks>
    /// Composed in one place and left <c>internal</c> so a unit test can assert the generated SQL with
    /// <c>ToQueryString()</c> — the ordering and the <c>TOP</c> must happen in the database, and that
    /// claim is worth checking without needing one. No <c>IQueryable</c> crosses the port itself.
    /// </remarks>
    internal static IQueryable<LatestMessageRow> LatestMessagesQuery(
        ChatDbContext context,
        ChatRoomId chatRoomId,
        int count) =>
        context.Messages
            .AsNoTracking()
            .Where(message => message.ChatRoomId == chatRoomId)
            .OrderByDescending(message => message.PostedAtUtc)
            .ThenByDescending(message => message.Id)
            .Take(count)
            .Select(message => new LatestMessageRow(
                message.Id,
                message.Author.DisplayName,
                message.Content,
                message.PostedAtUtc,
                message.Origin));

    /// <summary>
    /// One row of <see cref="LatestMessagesQuery"/>. It carries the value objects rather than their
    /// contents because EF Core cannot translate member access on a value-converted type, so the
    /// unwrapping to <see cref="MessageDto"/> happens once the rows are in memory.
    /// </summary>
    internal sealed record LatestMessageRow(
        MessageId Id,
        string AuthorDisplayName,
        MessageContent Content,
        DateTimeOffset PostedAtUtc,
        MessageOrigin Origin);
}
