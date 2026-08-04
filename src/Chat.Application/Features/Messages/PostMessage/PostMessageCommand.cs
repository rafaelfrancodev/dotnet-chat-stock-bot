using Chat.Application.Abstractions.Messaging;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.StockCommands;

namespace Chat.Application.Features.Messages.PostMessage;

/// <summary>
/// One line a participant typed into the chat box, exactly as they wrote it. The handler classifies it
/// with <see cref="ChatCommandParser"/> and either posts it or turns it into a bot request; the caller
/// never decides which.
/// </summary>
/// <remarks>
/// <b>Contract with the transport layer (the SignalR hub, task 1.12):</b> a client message carries only
/// <c>ChatRoomId</c> and <c>RawInput</c>. <c>AuthorUserId</c> and <c>AuthorDisplayName</c> are filled by
/// the hub from <c>Context.User</c> — the authenticated principal — and must never be read from the
/// client payload. Nothing in this layer can tell a forged author from a real one, so the only safe
/// place to establish identity is the edge that holds the authentication ticket.
/// <para>
/// This is why the command exposes no origin, no author flag and no message id: a caller cannot ask to
/// post as somebody else, cannot ask to post as the bot (<c>Message.PostByParticipant</c> rejects the
/// well-known bot author), and cannot choose the post time (it comes from the application clock).
/// </para>
/// </remarks>
/// <param name="ChatRoomId">Room to post into. Supplied by the client and checked against the store.</param>
/// <param name="RawInput">
/// Untrusted text, unparsed. May be a post, a <c>/stock=</c> command, or an unknown command.
/// </param>
/// <param name="AuthorUserId">
/// Authentication id of the poster, taken from the caller's claims — never from the client payload.
/// </param>
/// <param name="AuthorDisplayName">
/// Name rendered as the post owner, taken from the caller's claims — never from the client payload.
/// </param>
public sealed record PostMessageCommand(
    ChatRoomId ChatRoomId,
    string RawInput,
    string AuthorUserId,
    string AuthorDisplayName)
    : ICommand<PostMessageOutcome>
{
    /// <summary>Expected failures of this command, with stable codes for tests and clients.</summary>
    /// <remarks>
    /// Declared on the command rather than on the handler because the handler is internal: the request
    /// and the failures it can produce are the public surface of a feature. Failures raised by the value
    /// objects, by <c>Message</c> and by the parser are returned as-is and are not restated here.
    /// </remarks>
    public static class Errors
    {
        /// <summary>
        /// A slash command the chat does not implement. The message is fixed text: the command name the
        /// participant typed is untrusted and unbounded, so it is never echoed back into an error.
        /// </summary>
        public static readonly Error UnknownCommand = Error.Validation(
            "ChatCommand.Unknown",
            "Unknown command. The only command available is "
            + $"{ChatCommandParser.CommandPrefix}{ChatCommandParser.StockCommandName}{ChatCommandParser.ArgumentSeparator}"
            + "<code>, for example /stock=aapl.us.");
    }
}
