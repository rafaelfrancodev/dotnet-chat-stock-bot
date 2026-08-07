using Chat.Application.Abstractions.Realtime;
using Chat.Application.Contracts.Rooms;
using Chat.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Chat.Web.Realtime;

/// <summary>
/// Announces new rooms over SignalR. Lives in <c>Chat.Web</c> for the same reason as
/// <see cref="SignalRChatNotifier"/>: <see cref="IHubContext{THub}"/> belongs to the host that owns the hub.
/// </summary>
/// <param name="hubContext">Hub context used to reach every connection.</param>
internal sealed class SignalRChatRoomNotifier(IHubContext<ChatHub> hubContext) : IChatRoomNotifier
{
    public Task RoomCreatedAsync(ChatRoomDto room, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(room);

        // The one deliberate Clients.All in the application. It is the room directory, not a room's
        // traffic: a name and an identifier every signed-in participant is entitled to see, since the list
        // is what they pick from. The hub is [Authorize], so "everyone" means every authenticated
        // connection. See IChatRoomNotifier for why this is a separate port from IChatNotifier.
        return hubContext.Clients.All.SendAsync(ChatHub.ReceiveRoom, room, cancellationToken);
    }
}
