using Chat.Domain.Common;

namespace Chat.Application.Errors;

/// <summary>
/// Failures about a chat room that more than one use case produces. Shared deliberately: the code a
/// client switches on must be the same whether the room went missing while reading history, while
/// posting, or while the bot answered a quote.
/// </summary>
/// <remarks>
/// Not nested on a request type (the way single-feature failures are) precisely because it is not a
/// single feature's failure. It is also not on the <c>ChatRoom</c> aggregate: the aggregate cannot see
/// its own absence — only a repository lookup can — so "not found" is an Application-layer outcome.
/// </remarks>
public static class ChatRoomErrors
{
    /// <summary>
    /// The room does not exist. Handlers check this before doing any work, so an unknown room costs one
    /// existence query and never a write, a broadcast or a broker publish.
    /// </summary>
    public static readonly Error NotFound =
        Error.NotFound("ChatRoom.NotFound", "The chat room was not found.");
}
