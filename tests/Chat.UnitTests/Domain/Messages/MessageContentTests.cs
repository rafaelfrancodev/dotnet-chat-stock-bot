using Chat.Domain.Common;
using Chat.Domain.Messages;

namespace Chat.UnitTests.Domain.Messages;

public sealed class MessageContentTests
{
    [Fact]
    public void Create_EmptyContent_ReturnsFailure()
    {
        Result<MessageContent> result = MessageContent.Create(string.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MessageContent.Errors.Empty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("\t\n  ")]
    public void Create_WhitespaceOnly_ReturnsFailure(string? value)
    {
        Result<MessageContent> result = MessageContent.Create(value);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MessageContent.Errors.Empty);
    }

    [Fact]
    public void Create_TooLong_ReturnsFailure()
    {
        string tooLong = new('a', MessageConstants.MaxContentLength + 1);

        Result<MessageContent> result = MessageContent.Create(tooLong);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MessageContent.Errors.TooLong);
    }

    [Fact]
    public void Create_ExactlyMaxLength_Succeeds()
    {
        string atLimit = new('a', MessageConstants.MaxContentLength);

        Result<MessageContent> result = MessageContent.Create(atLimit);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().HaveLength(MessageConstants.MaxContentLength);
    }

    [Fact]
    public void Create_LengthExceededOnlyByWhitespace_TrimsAndSucceeds()
    {
        string padded = $"  {new string('a', MessageConstants.MaxContentLength)}  ";

        Result<MessageContent> result = MessageContent.Create(padded);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().HaveLength(MessageConstants.MaxContentLength);
    }

    [Fact]
    public void Create_ValidContent_TrimsAndSucceeds()
    {
        Result<MessageContent> result = MessageContent.Create("  hello room  ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be("hello room");
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        MessageContent first = MessageContent.Create("hello room").Value;
        MessageContent second = MessageContent.Create("  hello room ").Value;

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        MessageContent first = MessageContent.Create("hello room").Value;
        MessageContent second = MessageContent.Create("goodbye room").Value;

        first.Should().NotBe(second);
    }

    [Fact]
    public void ToString_ValidContent_ReturnsRawText()
    {
        MessageContent content = MessageContent.Create(" hello room ").Value;

        content.ToString().Should().Be("hello room");
    }
}
