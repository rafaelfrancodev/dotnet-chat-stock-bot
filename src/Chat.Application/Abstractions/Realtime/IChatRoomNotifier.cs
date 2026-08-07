using Chat.Application.Contracts.Rooms;

namespace Chat.Application.Abstractions.Realtime;

/// <summary>
/// Pushes changes to the <b>room directory</b> — which rooms exist — to every connected participant.
/// Implemented in Chat.Web over SignalR.
/// </summary>
/// <remarks>
/// Deliberately a separate port from <see cref="IChatNotifier"/>, which documents that it has no
/// broadcast-to-everyone member because a room's <i>traffic</i> must never reach connections that did not
/// join it. That guarantee stays intact: the directory is not a room's traffic. It carries a room's name
/// and identifier — facts every signed-in participant may see, since the list is what they choose from —
/// and no message, no author and no history.
/// <para>
/// Broadcasting is affordable here for a reason that does not hold for posts: rooms are created by hand,
/// so this fires a handful of times in a session rather than on every line typed.
/// </para>
/// </remarks>
public interface IChatRoomNotifier
{
    /// <summary>
    /// Announces a newly created room so open windows can offer it without a reload.
    /// </summary>
    /// <param name="room">The new room, already projected for the client.</param>
    /// <param name="cancellationToken">Cancels the broadcast.</param>
    Task RoomCreatedAsync(ChatRoomDto room, CancellationToken cancellationToken = default);
}
