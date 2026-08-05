using System.Security.Claims;
using Chat.Application.Contracts.Messages;
using Chat.Application.Features.Messages.GetLatestMessages;
using Chat.Application.Features.Messages.PostMessage;
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
/// <b>No per-connection server state.</b> The only membership this hub keeps is the SignalR group, which
/// SignalR itself removes when the connection ends; that is why <c>OnDisconnectedAsync</c> is not
/// overridden (see <see cref="JoinRoom"/>).
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
    /// SignalR does not preserve group membership across a reconnect, and this hub keeps no server-side
    /// map of connection to room to restore it (that would be exactly the unbounded per-connection state
    /// the resource budget rules out). The client re-joins from its <c>onreconnected</c> callback, which
    /// re-reads the history in the same call and so also fills whatever it missed while disconnected.
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
        await Groups
            .AddToGroupAsync(Context.ConnectionId, GroupFor(chatRoomId), Context.ConnectionAborted)
            .ConfigureAwait(false);

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
