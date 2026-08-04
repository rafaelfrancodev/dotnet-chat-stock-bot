using Chat.Application.Contracts.Messages;
using Chat.Domain.ChatRooms;

namespace Chat.Application.Abstractions.Realtime;

/// <summary>
/// Pushes a post to the participants of one room. Implemented in Chat.Web over SignalR.
/// </summary>
/// <remarks>
/// There is deliberately no "broadcast to everyone" member: every method takes a
/// <see cref="ChatRoomId"/>, so sending a room's traffic to unrelated connections is not expressible.
/// That is both a privacy boundary once multiple rooms exist and the resource-consumption control the
/// challenge asks for — implementations must target the room's SignalR group, never <c>Clients.All</c>.
/// </remarks>
public interface IChatNotifier
{
    /// <summary>Delivers a post to everyone currently connected to the given room.</summary>
    /// <param name="chatRoomId">Room whose group receives the post.</param>
    /// <param name="message">The post to render, already projected for the client.</param>
    /// <param name="cancellationToken">Cancels the broadcast.</param>
    Task BroadcastMessageAsync(
        ChatRoomId chatRoomId,
        MessageDto message,
        CancellationToken cancellationToken = default);
}
