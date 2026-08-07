using System.Security.Claims;
using Chat.Application.Contracts.Messages;
using Chat.Application.Contracts.Rooms;
using Chat.Application.Features.Messages.GetLatestMessages;
using Chat.Application.Features.Messages.PostMessage;
using Chat.Application.Features.Rooms.CreateRoom;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Chat.Web.Hubs;

/// <summary>
/// The realtime chat surface: join a room, receive its recent history, post a line, receive everybody
/// else's lines and the bot's answers.
/// </summary>
/// <remarks>
/// <b>Identity is never taken from the wire.</b> A client payload carries only a room id and the text
/// that was typed; the author is read from <see cref="HubCallerContext.User"/>, which is the
/// authentication cookie the framework already validated. That is why <c>PostMessageCommand</c> has no
/// origin flag and no message id — a caller cannot ask to post as somebody else or as the bot.
/// <para>
/// <b>Every method stays a transport adapter</b>: convert the wire types, read the claims, send one
/// request through <see cref="ISender"/>, map the <see cref="Result"/>. There is no chat rule in this
/// class, and errors are delivered to <see cref="IHubCallerClients{T}.Caller"/> only — never to the room.
/// </para>
/// <para>
/// <b>Per-connection server state is one room identifier, and only because switching rooms needs it.</b>
/// <see cref="JoinRoom"/> has to leave the group it was in, or a participant who switches keeps receiving
/// the old room's traffic. It is kept in <see cref="HubCallerContext.Items"/> — a single <see cref="Guid"/>
/// that SignalR frees along with the connection — rather than in a map this class would own and have to
/// clean up, which is why <c>OnDisconnectedAsync</c> is still not overridden. Asking the client which room
/// it is leaving would work only for as long as the client is correct; the server already knows.
/// </para>
/// </remarks>
/// <param name="sender">Dispatches the use cases. The hub owns no business logic of its own.</param>
[Authorize]
public sealed class ChatHub(ISender sender) : Hub
{
    /// <summary>Path the hub is mapped on. Shared so the page script and the tests cannot drift from it.</summary>
    public const string Route = "/hubs/chat";

    /// <summary>Client-side method invoked when a post is broadcast to a room.</summary>
    public const string ReceiveMessage = "ReceiveMessage";

    /// <summary>
    /// Client-side method invoked when the caller's own request failed. Sent to the caller alone: an
    /// unknown command or a rejected line is the sender's business, not the room's.
    /// </summary>
    public const string ReceiveError = "ReceiveError";

    /// <summary>
    /// Client-side method invoked with a <c>ChatAlert</c> when a system condition — rather than a chat
    /// answer — needs the participant's attention, such as the quote service being unreachable. Sent to
    /// one participant's connections, never to the room, and rendered as a banner instead of a post.
    /// </summary>
    public const string ReceiveAlert = "ReceiveAlert";

    /// <summary>
    /// Client-side method invoked with a <c>ChatRoomDto</c> when a room is created, so open windows can
    /// offer it without a reload. The room directory, not a room's traffic — see <c>IChatRoomNotifier</c>.
    /// </summary>
    public const string ReceiveRoom = "ReceiveRoom";

    /// <summary>
    /// Key under which this connection's current room is remembered, so <see cref="JoinRoom"/> can leave
    /// the previous group. Namespaced because <see cref="HubCallerContext.Items"/> is shared with anything
    /// else in the pipeline that writes to it.
    /// </summary>
    private const string CurrentRoomKey = "chat.currentRoom";

    /// <summary>Failures owned by the transport boundary itself.</summary>
    public static class Errors
    {
        /// <summary>
        /// The client sent something that is not a room identifier. Owned here rather than in
        /// Application because this layer is the one that turns a wire <see cref="Guid"/> into a
        /// <see cref="ChatRoomId"/>; rejecting it here also spares the pipeline a dispatch.
        /// </summary>
        public static readonly Error InvalidChatRoomId = Error.Validation(
            "ChatRoom.Invalid",
            "A chat room must be selected before joining or posting.");
    }

    /// <summary>
    /// SignalR group carrying one room's traffic. Broadcasts are always group-scoped — never
    /// <c>Clients.All</c> — so a room's messages never reach connections that did not join it.
    /// </summary>
    /// <param name="chatRoomId">Room whose group name is wanted.</param>
    public static string GroupFor(ChatRoomId chatRoomId) => $"room:{chatRoomId.Value}";

    /// <summary>
    /// Subscribes this connection to a room and returns the history the window opens with: the last
    /// <c>MessageConstants.LatestMessagesCount</c> posts, already oldest to newest.
    /// </summary>
    /// <remarks>
    /// <b>The group is joined before the history is read, deliberately.</b> Reading first would leave a
    /// window in which a post is committed after the read and before the subscription — a message the
    /// client never sees. In this order the worst case is a post that arrives both live and in the
    /// history, which the page removes by message id; a duplicate is recoverable, a gap is not.
    /// <para>
    /// The count is not a parameter. The query's own default is the challenge's 50 and its validator caps
    /// it at the same value, so no client can widen the read — neither the database nor the browser can
    /// be made to do more work by a crafted payload.
    /// </para>
    /// <para>
    /// SignalR does not preserve group membership across a reconnect, and a reconnect gets a new connection
    /// — so the remembered room goes with the old one and cannot be used to restore anything. The client
    /// re-joins from its <c>onreconnected</c> callback, which re-reads the history in the same call and so
    /// also fills whatever it missed while disconnected.
    /// </para>
    /// <para>
    /// A join the query then rejects — an unknown room — leaves this connection subscribed to that room's
    /// group and no longer in the previous one. Harmless: nothing can ever be broadcast to a room that does
    /// not exist, because posting checks the room first, and the only way to name one is a crafted payload,
    /// which costs the caller their own subscription and nobody else's.
    /// </para>
    /// </remarks>
    /// <param name="roomId">Room to join. Rejected when it is not a real identifier.</param>
    /// <returns>The room's latest posts, oldest first, or an empty list when the room was rejected.</returns>
    public async Task<IReadOnlyList<MessageDto>> JoinRoom(Guid roomId)
    {
        if (roomId == Guid.Empty)
        {
            return await FailAsync(Errors.InvalidChatRoomId).ConfigureAwait(false);
        }

        ChatRoomId chatRoomId = new(roomId);

        // Leaving first, and only when the room actually changes: a participant who switched rooms must
        // stop receiving the previous one's posts. Re-joining the same room (a reconnect) leaves the group
        // membership alone, so the reconnect path cannot drop the subscription it is trying to restore.
        await LeavePreviousRoomAsync(chatRoomId).ConfigureAwait(false);

        await Groups
            .AddToGroupAsync(Context.ConnectionId, GroupFor(chatRoomId), Context.ConnectionAborted)
            .ConfigureAwait(false);

        Context.Items[CurrentRoomKey] = roomId;

        Result<IReadOnlyList<MessageDto>> history = await sender
            .Send(new GetLatestMessagesQuery(chatRoomId), Context.ConnectionAborted)
            .ConfigureAwait(false);

        return history.IsSuccess
            ? history.Value
            : await FailAsync(history.Error).ConfigureAwait(false);
    }

    /// <summary>
    /// Posts one line of chat input. The line may be an ordinary post or a <c>/stock=</c> command; the
    /// hub does not decide which, and never gets to write to the message store either way.
    /// </summary>
    /// <remarks>
    /// <b>Nothing is returned to the caller on success.</b> An ordinary post has already been broadcast
    /// to the room group — which includes the sender's own connection — so echoing it here would render
    /// it twice in the sender's window; a stock command has no answer yet, because the bot's reply
    /// arrives later over the broker as an ordinary broadcast.
    /// </remarks>
    /// <param name="roomId">Room to post into.</param>
    /// <param name="text">Exactly what the participant typed. Untrusted, unparsed, length-checked by the use case.</param>
    public async Task SendMessage(Guid roomId, string text)
    {
        if (roomId == Guid.Empty)
        {
            await SendErrorAsync(Errors.InvalidChatRoomId).ConfigureAwait(false);
            return;
        }

        PostMessageCommand command = new(new ChatRoomId(roomId), text, CurrentUserId(), CurrentDisplayName());
        Result<PostMessageOutcome> result =
            await sender.Send(command, Context.ConnectionAborted).ConfigureAwait(false);

        if (result.IsFailure)
        {
            await SendErrorAsync(result.Error).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates a chat room and returns it, so the caller can switch to it straight away.
    /// </summary>
    /// <remarks>
    /// The caller is <b>not</b> joined to the new room here. Creating and joining are separate acts — a
    /// participant may create a room and stay where they are — and doing both in one call would also mean
    /// this method had two reasons to fail with one return value. The client calls <c>JoinRoom</c> next.
    /// <para>
    /// Every other window learns about the room through <c>ReceiveRoom</c>, broadcast by the use case after
    /// it commits, not from this return value: the creator's own window is not the only one that needs the
    /// list updated.
    /// </para>
    /// </remarks>
    /// <param name="name">The requested name. Untrusted and unnormalised; <c>RoomName</c> owns both.</param>
    /// <returns>The created room, or <see langword="null"/> when the request was refused.</returns>
    public async Task<ChatRoomDto?> CreateRoom(string name)
    {
        Result<ChatRoomDto> created = await sender
            .Send(new CreateRoomCommand(name), Context.ConnectionAborted)
            .ConfigureAwait(false);

        if (created.IsSuccess)
        {
            return created.Value;
        }

        await SendErrorAsync(created.Error).ConfigureAwait(false);

        return null;
    }

    /// <summary>
    /// Removes this connection from the room it was in, unless that is the room being joined.
    /// </summary>
    /// <remarks>
    /// The guard is what keeps a reconnect safe: re-joining the same room must not remove the membership
    /// the reconnect is restoring.
    /// </remarks>
    /// <param name="joining">The room about to be joined.</param>
    private async Task LeavePreviousRoomAsync(ChatRoomId joining)
    {
        // TryGetValue, not the indexer: Items is a dictionary, so indexing an absent key throws — and it is
        // absent on the first join of every connection, which is the common case.
        if (!Context.Items.TryGetValue(CurrentRoomKey, out object? current)
            || current is not Guid previous
            || previous == joining.Value)
        {
            return;
        }

        await Groups
            .RemoveFromGroupAsync(Context.ConnectionId, GroupFor(new ChatRoomId(previous)), Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Authentication id of the caller, from the validated ticket. Empty only if a principal somehow
    /// carries no subject, which <c>PostMessageValidator</c> then rejects as a failed result.
    /// </summary>
    private string CurrentUserId() => Context.UserIdentifier ?? string.Empty;

    /// <summary>
    /// Name rendered as the post owner. Read from the <c>display_name</c> claim issued at sign-in by
    /// <c>DisplayNameClaimsPrincipalFactory</c>, so posting costs no <c>AspNetUsers</c> query. The user
    /// name is the fallback for a ticket issued before that claim existed.
    /// </summary>
    private string CurrentDisplayName() =>
        Context.User?.FindFirstValue(ChatClaimTypes.DisplayName)
        ?? Context.User?.Identity?.Name
        ?? string.Empty;

    /// <summary>
    /// Reports an expected failure to the caller and to nobody else. Only curated
    /// <see cref="Error.Message"/> text is sent: the use cases never put untrusted input into an error,
    /// and an unexpected exception is turned into SignalR's own generic message instead of this one.
    /// </summary>
    private Task SendErrorAsync(Error error) =>
        Clients.Caller.SendAsync(ReceiveError, error.Message, Context.ConnectionAborted);

    /// <summary>Reports a failure and answers a history request with nothing.</summary>
    private async Task<IReadOnlyList<MessageDto>> FailAsync(Error error)
    {
        await SendErrorAsync(error).ConfigureAwait(false);
        return [];
    }
}
