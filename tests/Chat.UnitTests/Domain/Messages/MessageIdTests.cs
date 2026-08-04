using Chat.Domain.Messages;

namespace Chat.UnitTests.Domain.Messages;

public sealed class MessageIdTests
{
    [Fact]
    public void New_Always_ReturnsDistinctNonEmptyIds()
    {
        MessageId first = MessageId.New();
        MessageId second = MessageId.New();

        first.Value.Should().NotBe(Guid.Empty);
        first.Should().NotBe(second);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        Guid value = Guid.CreateVersion7();

        new MessageId(value).Should().Be(new MessageId(value));
    }
}
