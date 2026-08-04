using Chat.Application.Abstractions.Persistence;
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
}
