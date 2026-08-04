using Chat.Domain.ChatRooms;

namespace Chat.UnitTests.Domain.ChatRooms;

public sealed class ChatRoomIdTests
{
    [Fact]
    public void New_Always_ReturnsDistinctNonEmptyIds()
    {
        ChatRoomId first = ChatRoomId.New();
        ChatRoomId second = ChatRoomId.New();

        first.Value.Should().NotBe(Guid.Empty);
        first.Should().NotBe(second);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        Guid value = Guid.CreateVersion7();

        new ChatRoomId(value).Should().Be(new ChatRoomId(value));
    }
}
