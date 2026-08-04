using Chat.Domain.ChatRooms;
using Chat.Domain.Common;

namespace Chat.Domain.Messages;

/// <summary>
/// A post in a chat room, and its own aggregate root: the only write is "append one post" and the only
/// read is "the last 50 of a room", so a message is never loaded as part of a <c>ChatRoom</c>.
/// <para>
/// The room is referenced by <see cref="ChatRoomId"/> alone — there is no navigation property, so no
/// caller can walk from a message to a room and no N+1 access pattern is even expressible.
/// </para>
/// <para>
/// A <c>/stock=</c> command is a command, not a post: it is recognised by
/// <see cref="StockCommands.ChatCommandParser"/> before this type is ever reached and is never turned
/// into a <see cref="Message"/>.
/// </para>
/// </summary>
public sealed class Message : AggregateRoot<MessageId>
{
    private Message(
        MessageId id,
        ChatRoomId chatRoomId,
        MessageAuthor author,
        MessageContent content,
        MessageOrigin origin,
        DateTimeOffset postedAtUtc)
        : base(id)
    {
        ChatRoomId = chatRoomId;
        Author = author;
        Content = content;
        Origin = origin;
        PostedAtUtc = postedAtUtc;
    }

    /// <summary>
    /// Materialisation constructor for the persistence layer only. EF Core bypasses the factories and
    /// writes every property (through their backing fields) immediately after calling it, so the
    /// null-forgiving assignments below are never observable. Owned value objects cannot be bound
    /// through a parameterised constructor, which is why this one exists.
    /// </summary>
    private Message()
        : base(default)
    {
        Author = null!;
        Content = null!;
    }

    /// <summary>Room this post belongs to. Referenced by id: aggregates never point at each other.</summary>
    public ChatRoomId ChatRoomId { get; private init; }

    /// <summary>Post owner — a participant, or the well-known <see cref="MessageAuthor.Bot"/>.</summary>
    public MessageAuthor Author { get; private init; }

    /// <summary>The text of the post, already trimmed and length-checked.</summary>
    public MessageContent Content { get; private init; }

    /// <summary>Whether a participant or the stock bot produced this post.</summary>
    public MessageOrigin Origin { get; private init; }

    /// <summary>
    /// When the post was made, normalised to UTC. Sole ordering key of the "last 50" query, so it is
    /// supplied by the caller (an <c>IDateTimeProvider</c>) rather than read from the clock here.
    /// </summary>
    public DateTimeOffset PostedAtUtc { get; private init; }

    /// <summary>Posts a participant's message and raises <see cref="MessagePosted"/>.</summary>
    /// <param name="chatRoomId">Room to post into. Must not be the default id.</param>
    /// <param name="author">Post owner, taken from the caller's claims. Must not be the bot.</param>
    /// <param name="content">The validated text.</param>
    /// <param name="postedAtUtc">Post time, supplied by the application clock. Must not be the default value.</param>
    public static Result<Message> PostByParticipant(
        ChatRoomId chatRoomId,
        MessageAuthor author,
        MessageContent content,
        DateTimeOffset postedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(content);

        // Keeps Origin and Author consistent: only PostByBot may produce a bot-owned post.
        return author.IsBot
            ? Result.Failure<Message>(Errors.BotAuthorNotAllowed)
            : Post(chatRoomId, author, content, MessageOrigin.Participant, postedAtUtc);
    }

    /// <summary>
    /// Posts the bot's answer to a stock command and raises <see cref="MessagePosted"/>. The owner is
    /// fixed to <see cref="MessageAuthor.Bot"/>: the challenge requires the bot to own its posts, so
    /// the author is not something a caller can supply.
    /// </summary>
    /// <param name="chatRoomId">Room the quote was requested in. Must not be the default id.</param>
    /// <param name="content">The ready-to-post quote text produced by the bot.</param>
    /// <param name="postedAtUtc">Post time, supplied by the application clock. Must not be the default value.</param>
    public static Result<Message> PostByBot(ChatRoomId chatRoomId, MessageContent content, DateTimeOffset postedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(content);

        return Post(chatRoomId, MessageAuthor.Bot, content, MessageOrigin.Bot, postedAtUtc);
    }

    private static Result<Message> Post(
        ChatRoomId chatRoomId,
        MessageAuthor author,
        MessageContent content,
        MessageOrigin origin,
        DateTimeOffset postedAtUtc)
    {
        if (chatRoomId == default)
        {
            return Result.Failure<Message>(Errors.MissingChatRoom);
        }

        if (postedAtUtc == default)
        {
            return Result.Failure<Message>(Errors.MissingPostTime);
        }

        // Normalised here so the ordering key cannot drift with the caller's offset; the instant is unchanged.
        Message message = new(MessageId.New(), chatRoomId, author, content, origin, postedAtUtc.ToUniversalTime());
        message.Raise(new MessagePosted(message.Id, message.ChatRoomId, author, content, message.PostedAtUtc));

        return Result.Success(message);
    }

    /// <summary>Expected failures of the factories, with stable codes for tests and clients.</summary>
    public static class Errors
    {
        /// <summary>The room id was the default value, which identifies no room.</summary>
        public static readonly Error MissingChatRoom =
            Error.Validation("Message.MissingChatRoom", "A message must belong to a chat room.");

        /// <summary>The post time was the default value, which would corrupt the message ordering.</summary>
        public static readonly Error MissingPostTime =
            Error.Validation("Message.MissingPostTime", "A message must have a post time.");

        /// <summary>A participant post was attempted with the bot as its author.</summary>
        public static readonly Error BotAuthorNotAllowed = Error.Validation(
            "Message.BotAuthorNotAllowed",
            "The bot author is reserved for bot posts; use Message.PostByBot.");
    }
}
