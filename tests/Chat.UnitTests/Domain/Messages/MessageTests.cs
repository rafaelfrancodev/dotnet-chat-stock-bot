using System.Reflection;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.Messages;

namespace Chat.UnitTests.Domain.Messages;

public sealed class MessageTests
{
    private static readonly ChatRoomId RoomId = ChatRoomId.New();
    private static readonly DateTimeOffset PostedAt = new(2026, 8, 4, 10, 30, 0, TimeSpan.Zero);

    private static MessageAuthor Participant => MessageAuthor.Create("user-1", "Alice").Value;

    private static MessageContent Content => MessageContent.Create("hello room").Value;

    [Fact]
    public void PostByParticipant_ValidInput_RaisesMessagePosted()
    {
        Result<Message> result = Message.PostByParticipant(RoomId, Participant, Content, PostedAt);

        result.IsSuccess.Should().BeTrue();
        Message message = result.Value;
        message.ChatRoomId.Should().Be(RoomId);
        message.Author.Should().Be(Participant);
        message.Content.Should().Be(Content);
        message.Origin.Should().Be(MessageOrigin.Participant);
        message.PostedAtUtc.Should().Be(PostedAt);
        message.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<MessagePosted>();
    }

    [Fact]
    public void PostByParticipant_ValidInput_DoesNotProduceABotAuthor()
    {
        Message message = Message.PostByParticipant(RoomId, Participant, Content, PostedAt).Value;

        message.Author.IsBot.Should().BeFalse();
        message.Origin.Should().NotBe(MessageOrigin.Bot);
    }

    [Fact]
    public void PostByParticipant_BotAuthor_ReturnsFailure()
    {
        Result<Message> result = Message.PostByParticipant(RoomId, MessageAuthor.Bot, Content, PostedAt);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Message.Errors.BotAuthorNotAllowed);
    }

    [Fact]
    public void PostByBot_ValidInput_SetsBotAuthorAndOrigin()
    {
        Result<Message> result = Message.PostByBot(RoomId, Content, PostedAt);

        result.IsSuccess.Should().BeTrue();
        Message message = result.Value;
        message.Author.Should().Be(MessageAuthor.Bot);
        message.Author.IsBot.Should().BeTrue();
        message.Author.DisplayName.Should().Be(MessageAuthor.BotDisplayName);
        message.Origin.Should().Be(MessageOrigin.Bot);
    }

    [Theory]
    [InlineData(MessageOrigin.Participant)]
    [InlineData(MessageOrigin.Bot)]
    public void Post_ValidInput_RaisesExactlyOneEventCarryingIdsAndContent(MessageOrigin origin)
    {
        Message message = Post(origin, RoomId, PostedAt).Value;

        MessagePosted posted = message.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<MessagePosted>().Subject;
        posted.MessageId.Should().Be(message.Id);
        posted.ChatRoomId.Should().Be(RoomId);
        posted.Author.Should().Be(message.Author);
        posted.Content.Should().Be(Content);
        posted.OccurredAtUtc.Should().Be(message.PostedAtUtc);
    }

    [Fact]
    public void ClearDomainEvents_AfterPost_RemovesTheRecordedEvent()
    {
        Message message = Message.PostByBot(RoomId, Content, PostedAt).Value;

        message.ClearDomainEvents();

        message.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MessageOrigin.Participant)]
    [InlineData(MessageOrigin.Bot)]
    public void Post_DefaultChatRoomId_ReturnsFailure(MessageOrigin origin)
    {
        Result<Message> result = Post(origin, default, PostedAt);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Message.Errors.MissingChatRoom);
    }

    [Theory]
    [InlineData(MessageOrigin.Participant)]
    [InlineData(MessageOrigin.Bot)]
    public void Post_DefaultPostTime_ReturnsFailure(MessageOrigin origin)
    {
        Result<Message> result = Post(origin, RoomId, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Message.Errors.MissingPostTime);
    }

    [Fact]
    public void Post_NonUtcPostTime_NormalisesToUtcWithoutChangingTheInstant()
    {
        DateTimeOffset localTime = new(2026, 8, 4, 12, 30, 0, TimeSpan.FromHours(2));

        Message message = Message.PostByBot(RoomId, Content, localTime).Value;

        message.PostedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        message.PostedAtUtc.Should().Be(localTime);
    }

    [Fact]
    public void Post_TwoMessages_GetDistinctIdentities()
    {
        Message first = Message.PostByBot(RoomId, Content, PostedAt).Value;
        Message second = Message.PostByBot(RoomId, Content, PostedAt).Value;

        first.Id.Should().NotBe(second.Id);
        first.Should().NotBe(second);
    }

    [Fact]
    public void Constructors_AreAllNonPublic_SoTheFactoriesAreTheOnlyEntryPoint()
    {
        ConstructorInfo[] constructors = typeof(Message)
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        constructors.Should().NotBeEmpty();
        constructors.Should().OnlyContain(constructor => constructor.IsPrivate);
    }

    private static Result<Message> Post(MessageOrigin origin, ChatRoomId chatRoomId, DateTimeOffset postedAtUtc) =>
        origin == MessageOrigin.Bot
            ? Message.PostByBot(chatRoomId, Content, postedAtUtc)
            : Message.PostByParticipant(chatRoomId, Participant, Content, postedAtUtc);
}
