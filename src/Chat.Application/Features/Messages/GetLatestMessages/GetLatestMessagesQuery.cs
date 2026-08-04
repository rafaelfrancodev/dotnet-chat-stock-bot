using Chat.Application.Abstractions.Messaging;
using Chat.Application.Contracts.Messages;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.Messages;

namespace Chat.Application.Features.Messages.GetLatestMessages;

/// <summary>
/// Reads the history a chat window opens with: the most recent posts of one room, already ordered
/// oldest to newest so the caller renders them top to bottom without re-sorting.
/// </summary>
/// <param name="ChatRoomId">Room whose history is wanted.</param>
/// <param name="Count">
/// How many posts to return. Defaults to <see cref="MessageConstants.LatestMessagesCount"/> (50), the
/// challenge requirement, and is bounded by the same value: an unbounded caller-supplied count would be
/// a denial-of-service vector against the database, and no client is allowed to render more than 50.
/// </param>
public sealed record GetLatestMessagesQuery(
    ChatRoomId ChatRoomId,
    int Count = MessageConstants.LatestMessagesCount)
    : IQuery<IReadOnlyList<MessageDto>>
{
    /// <summary>Expected failures of this query, with stable codes for tests and clients.</summary>
    /// <remarks>
    /// Declared on the query rather than on the handler because the handler is internal: the request and
    /// the failures it can produce are the public surface of the feature.
    /// </remarks>
    public static class Errors
    {
        /// <summary>The requested room does not exist, so there is no history to read.</summary>
        public static readonly Error ChatRoomNotFound =
            Error.NotFound("ChatRoom.NotFound", "The chat room was not found.");
    }
}
