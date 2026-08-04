using Chat.Application.Abstractions.Persistence;

namespace Chat.Infrastructure.Persistence.Repositories;

/// <summary>
/// The one place a chat write reaches the database. Kept separate from the repositories so that
/// "this use case performs no write" stays provable: no repository can commit behind a handler's back.
/// </summary>
/// <param name="context">The chat database context, scoped to the current unit of work.</param>
internal sealed class UnitOfWork(ChatDbContext context) : IUnitOfWork
{
    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
