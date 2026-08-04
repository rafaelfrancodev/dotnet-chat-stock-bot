using Chat.Domain.ChatRooms;
using Chat.Domain.Common;

namespace Chat.Domain.Messages;

/// <summary>
/// A post was accepted into a room. Raised by <see cref="Message.PostByParticipant"/> and
/// <see cref="Message.PostByBot"/>, dispatched by the Application layer <b>after</b> the unit of work
/// commits, so a failed save can never produce a phantom broadcast.
/// </summary>
/// <param name="MessageId">Identity of the new post.</param>
/// <param name="ChatRoomId">Room the post belongs to — the SignalR group the broadcast targets.</param>
/// <param name="Author">Post owner; <see cref="MessageAuthor.IsBot"/> distinguishes bot answers.</param>
/// <param name="Content">The text to render.</param>
/// <param name="OccurredAtUtc">When the post was made, in UTC. Same instant as <see cref="Message.PostedAtUtc"/>.</param>
public sealed record MessagePosted(
    MessageId MessageId,
    ChatRoomId ChatRoomId,
    MessageAuthor Author,
    MessageContent Content,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
