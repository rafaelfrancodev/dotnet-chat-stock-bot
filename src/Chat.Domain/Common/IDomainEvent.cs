namespace Chat.Domain.Common;

/// <summary>
/// Something that happened in the domain. Deliberately framework-free: the Application layer
/// adapts these to MediatR notifications so the Domain keeps zero external dependencies.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAtUtc { get; }
}
