using Chat.Application.Abstractions.Realtime;
using Chat.Application.Contracts.Messages;
using Chat.Application.Contracts.Realtime;
using Chat.Domain.ChatRooms;
using Chat.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Chat.Web.Realtime;

/// <summary>
/// Broadcasts posts over SignalR. Implemented in <c>Chat.Web</c> rather than Infrastructure because
/// <see cref="IHubContext{THub}"/> belongs to the host that owns the hub.
/// </summary>
/// <param name="hubContext">Hub context used to reach the room's group.</param>
internal sealed class SignalRChatNotifier(IHubContext<ChatHub> hubContext) : IChatNotifier
{
    public Task BroadcastMessageAsync(
        ChatRoomId chatRoomId,
        MessageDto message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Group-scoped, never Clients.All: uninvolved connections receive nothing, which is both the
        // privacy boundary once several rooms exist and the resource control the challenge asks for.
        return hubContext.Clients
            .Group(ChatHub.GroupFor(chatRoomId))
            .SendAsync(ChatHub.ReceiveMessage, message, cancellationToken);
    }

    public Task NotifyAlertAsync(
        string userId,
        ChatAlert alert,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(alert);

        // Clients.User reaches every connection that participant has open — two browser tabs both get
        // the banner — and reaches none of anybody else's. Identity supplies the user id, so this cannot
        // be aimed at another participant by a client.
        return hubContext.Clients
            .User(userId)
            .SendAsync(ChatHub.ReceiveAlert, alert, cancellationToken);
    }
}
