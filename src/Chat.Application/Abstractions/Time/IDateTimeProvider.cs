namespace Chat.Application.Abstractions.Time;

/// <summary>
/// The application clock. Every post time and domain-event timestamp comes from here, never from
/// <c>DateTimeOffset.UtcNow</c> read inline.
/// </summary>
/// <remarks>
/// The domain factories (<c>Message.PostByParticipant</c>, <c>ChatRoom.Create</c>) take the instant as a
/// parameter precisely so that handlers can supply it from this port and tests can pin it. Reading the
/// clock directly would make message ordering — a mandatory requirement — untestable.
/// <para>
/// This is the one abstraction in the layer with no asynchronous method: reading a clock does no I/O,
/// so a <c>Task</c>-returning signature would be a lie.
/// </para>
/// </remarks>
public interface IDateTimeProvider
{
    /// <summary>The current instant, always at offset zero.</summary>
    DateTimeOffset UtcNow { get; }
}
