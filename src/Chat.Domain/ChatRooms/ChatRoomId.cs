namespace Chat.Domain.ChatRooms;

/// <summary>
/// Strongly-typed identifier of a <c>ChatRoom</c>. Messages reference their room by this id only —
/// aggregates never hold references to other aggregates.
/// </summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct ChatRoomId(Guid Value)
{
    /// <summary>
    /// Creates a new identifier. Version 7 GUIDs are time-ordered, which keeps inserts sequential in
    /// the clustered index instead of fragmenting it like random GUIDs do.
    /// </summary>
    public static ChatRoomId New() => new(Guid.CreateVersion7());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
