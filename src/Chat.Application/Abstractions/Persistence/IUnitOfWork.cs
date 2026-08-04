namespace Chat.Application.Abstractions.Persistence;

/// <summary>
/// Commits everything the repositories have staged, in one transaction.
/// </summary>
/// <remarks>
/// The split is deliberate and handlers must respect it: repositories only <c>Add</c>, and a use case
/// is not persisted until it calls <see cref="SaveChangesAsync"/> exactly once. That keeps a handler's
/// writes atomic and makes "this path performs no write" provable in a test — no repository method can
/// commit behind the handler's back.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Commits the staged changes.</summary>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns>The number of affected rows.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
