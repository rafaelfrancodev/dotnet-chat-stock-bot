namespace Chat.Domain.Messages;

/// <summary>
/// Strongly-typed identifier of a <c>Message</c>. Being a distinct type, it cannot be swapped with a
/// <see cref="Chat.Domain.ChatRooms.ChatRoomId"/> by accident.
/// </summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct MessageId(Guid Value)
{
    /// <summary>
    /// Creates a new identifier. Version 7 GUIDs are time-ordered, which keeps inserts sequential in
    /// the clustered index instead of fragmenting it like random GUIDs do.
    /// </summary>
    public static MessageId New() => new(Guid.CreateVersion7());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
