using Chat.Domain.ChatRooms;
using Microsoft.AspNetCore.SignalR;

namespace Chat.Web.Hubs;

/// <summary>
/// The realtime chat surface.
/// </summary>
/// <remarks>
/// Deliberately empty for now: task 1.11 wires authentication and task 1.12 adds <c>JoinRoom</c>,
/// <c>SendMessage</c> and the <c>[Authorize]</c> attribute. The type exists already so
/// <see cref="Realtime.SignalRChatNotifier"/> has a hub to target, which is what lets
/// <c>PostMessageHandler</c> resolve its notifier.
/// <para>
/// The endpoint is <b>not mapped</b> in <c>Program.cs</c> until 1.12: mapping it now, before
/// authentication exists, would expose an unauthenticated realtime surface.
/// </para>
/// </remarks>
public sealed class ChatHub : Hub
{
    /// <summary>Client-side method invoked when a post is broadcast to a room.</summary>
    public const string ReceiveMessage = "ReceiveMessage";

    /// <summary>
    /// SignalR group carrying one room's traffic. Broadcasts are always group-scoped — never
    /// <c>Clients.All</c> — so a room's messages never reach connections that did not join it.
    /// </summary>
    /// <param name="chatRoomId">Room whose group name is wanted.</param>
    public static string GroupFor(ChatRoomId chatRoomId) => $"room:{chatRoomId.Value}";
}
