namespace Chat.Domain.ChatRooms;

/// <summary>Domain-wide constants for chat rooms. No magic strings anywhere else.</summary>
public static class ChatRoomConstants
{
    /// <summary>
    /// Name of the room every participant lands in. Declared here rather than in the seeder because two
    /// sides now depend on it agreeing: startup creates a room with this name, and the chat page looks
    /// one up by it. A second copy of the literal would let them drift into an empty chat window.
    /// </summary>
    public const string DefaultRoomName = "General";
}
