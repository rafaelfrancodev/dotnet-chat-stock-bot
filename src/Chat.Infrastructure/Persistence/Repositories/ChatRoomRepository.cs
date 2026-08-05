using Chat.Application.Abstractions.Persistence;
using Chat.Application.Contracts.Rooms;
using Chat.Domain.ChatRooms;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IChatRoomRepository"/>.</summary>
/// <param name="context">The chat database context, scoped to the current unit of work.</param>
internal sealed class ChatRoomRepository(ChatDbContext context) : IChatRoomRepository
{
    /// <inheritdoc/>
    public void Add(ChatRoom chatRoom)
    {
        ArgumentNullException.ThrowIfNull(chatRoom);

        // Staged only. Nothing is written until IUnitOfWork.SaveChangesAsync commits.
        context.ChatRooms.Add(chatRoom);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// An existence query, not a load: the caller wants a boolean, and pulling the aggregate back to
    /// answer it would read columns nobody looks at on every single post.
    /// </remarks>
    public Task<bool> ExistsAsync(ChatRoomId chatRoomId, CancellationToken cancellationToken = default) =>
        context.ChatRooms.AnyAsync(room => room.Id == chatRoomId, cancellationToken);

    /// <inheritdoc/>
    public async Task<ChatRoomDto?> FindByNameAsync(RoomName name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        ChatRoomRow? row = await RoomByNameQuery(context, name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : new ChatRoomDto(row.Id.Value, row.Name.Value);
    }

    /// <summary>
    /// The "room with this name" query: no tracking, no entity materialisation, and only the two columns
    /// a caller renders. The unique index on <c>ChatRooms.Name</c> serves the filter.
    /// </summary>
    /// <remarks>
    /// Composed in one place and left <c>internal</c> so a unit test can assert the generated SQL with
    /// <c>ToQueryString()</c>, the same way the latest-messages query is checked. No <c>IQueryable</c>
    /// crosses the port itself.
    /// </remarks>
    internal static IQueryable<ChatRoomRow> RoomByNameQuery(ChatDbContext context, RoomName name) =>
        context.ChatRooms
            .AsNoTracking()
            .Where(room => room.Name == name)
            .Select(room => new ChatRoomRow(room.Id, room.Name));

    /// <summary>
    /// One row of <see cref="RoomByNameQuery"/>. It carries the value objects rather than their contents
    /// because EF Core cannot translate member access on a value-converted type, so the unwrapping to
    /// <see cref="ChatRoomDto"/> happens once the row is in memory.
    /// </summary>
    internal sealed record ChatRoomRow(ChatRoomId Id, RoomName Name);
}
