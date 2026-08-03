namespace Chat.Domain.Common;

/// <summary>Base class for entities: identity-based equality on <typeparamref name="TId"/>.</summary>
public abstract class Entity<TId>
    where TId : notnull
{
    protected Entity(TId id) => Id = id;

    public TId Id { get; protected init; } = default!;

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && other.GetType() == GetType() && EqualityComparer<TId>.Default.Equals(other.Id, Id);

    public override int GetHashCode() => EqualityComparer<TId>.Default.GetHashCode(Id);
}
