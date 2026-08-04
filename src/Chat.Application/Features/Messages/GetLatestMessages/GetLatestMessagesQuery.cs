using Chat.Application.Abstractions.Messaging;
using Chat.Application.Contracts.Messages;
using Chat.Domain.ChatRooms;
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
    : IQuery<IReadOnlyList<MessageDto>>;
