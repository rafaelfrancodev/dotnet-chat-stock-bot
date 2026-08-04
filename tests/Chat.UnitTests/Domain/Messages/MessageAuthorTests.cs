using Chat.Domain.Common;
using Chat.Domain.Messages;

namespace Chat.UnitTests.Domain.Messages;

public sealed class MessageAuthorTests
{
    [Fact]
    public void Create_ValidInput_TrimsAndSucceeds()
    {
        Result<MessageAuthor> result = MessageAuthor.Create("  user-1 ", "  Alice  ");

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be("user-1");
        result.Value.DisplayName.Should().Be("Alice");
        result.Value.IsBot.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyUserId_ReturnsFailure(string? userId)
    {
        Result<MessageAuthor> result = MessageAuthor.Create(userId, "Alice");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MessageAuthor.Errors.EmptyUserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyDisplayName_ReturnsFailure(string? displayName)
    {
        Result<MessageAuthor> result = MessageAuthor.Create("user-1", displayName);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MessageAuthor.Errors.EmptyDisplayName);
    }

    [Fact]
    public void Bot_WellKnownInstance_OwnsStockQuotePosts()
    {
        MessageAuthor bot = MessageAuthor.Bot;

        bot.UserId.Should().Be(MessageAuthor.BotUserId);
        bot.DisplayName.Should().Be(MessageAuthor.BotDisplayName);
        bot.IsBot.Should().BeTrue();
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        MessageAuthor first = MessageAuthor.Create("user-1", "Alice").Value;
        MessageAuthor second = MessageAuthor.Create(" user-1 ", " Alice ").Value;

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentUserId_AreNotEqual()
    {
        MessageAuthor first = MessageAuthor.Create("user-1", "Alice").Value;
        MessageAuthor second = MessageAuthor.Create("user-2", "Alice").Value;

        first.Should().NotBe(second);
    }
}
