using Chat.Application.Abstractions.Time;

namespace Chat.Infrastructure.Time;

/// <summary>
/// The real clock behind <see cref="IDateTimeProvider"/>. Stateless and thread-safe, so it is
/// registered as a singleton.
/// </summary>
internal sealed class SystemDateTimeProvider : IDateTimeProvider
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
