using Chat.Application.Contracts.Messages;
using Chat.Application.Contracts.Realtime;
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

    /// <summary>
    /// Delivers a system condition to one participant — not to the room.
    /// </summary>
    /// <remarks>
    /// Targeted rather than broadcast on purpose: the participant who typed <c>/stock=</c> is the one
    /// waiting on an answer, and telling everyone else that somebody's lookup failed is noise. It is a
    /// separate channel from <see cref="BroadcastMessageAsync"/> because an alert is not a post: it is
    /// never persisted, never appears in the last-50 history, and is rendered as a banner.
    /// </remarks>
    /// <param name="userId">Authentication user id of the recipient, taken from server-side claims.</param>
    /// <param name="alert">The curated alert. Must never contain participant-supplied text.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    Task NotifyAlertAsync(
        string userId,
        ChatAlert alert,
        CancellationToken cancellationToken = default);
}
