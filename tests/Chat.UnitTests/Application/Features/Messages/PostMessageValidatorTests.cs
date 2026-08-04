using Chat.Application.Features.Messages.PostMessage;
using Chat.Domain.ChatRooms;
using Chat.Domain.Messages;
using FluentValidation.Results;

namespace Chat.UnitTests.Application.Features.Messages;

public sealed class PostMessageValidatorTests
{
    private static readonly ChatRoomId RoomId = ChatRoomId.New();

    private readonly PostMessageValidator _validator = new();

    private static PostMessageCommand Command(
        string rawInput = "hello team",
        string userId = "user-1",
        string displayName = "Alice") =>
        new(RoomId, rawInput, userId, displayName);

    [Theory]
    [InlineData("hello team")]
    [InlineData("/stock=aapl.us")]
    public void Validate_UsableInput_IsValid(string rawInput)
    {
        // A command is as valid a line as a post: classification is the handler's job, not the validator's.
        ValidationResult result = _validator.Validate(Command(rawInput));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DefaultRoomId_IsRejected()
    {
        ValidationResult result = _validator.Validate(new PostMessageCommand(default, "hello team", "user-1", "Alice"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(PostMessageCommand.ChatRoomId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n  ")]
    public void Validate_EmptyOrWhitespaceRawInput_IsRejected(string? rawInput)
    {
        ValidationResult result = _validator.Validate(Command(rawInput!));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(failure => failure.PropertyName == nameof(PostMessageCommand.RawInput));
    }

    [Fact]
    public void Validate_RawInputAboveTheCap_IsRejected()
    {
        // Bounds the work one line can cause before the parser or any value object ever sees it.
        ValidationResult result = _validator.Validate(Command(new string('a', PostMessageValidator.MaxRawInputLength + 1)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(PostMessageCommand.RawInput));
    }

    [Fact]
    public void Validate_RawInputOnTheBoundary_IsAccepted()
    {
        ValidationResult result = _validator.Validate(Command(new string('a', PostMessageValidator.MaxRawInputLength)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MaximumLengthSurroundedByWhitespace_IsAccepted()
    {
        // The cap is measured after trimming, exactly as MessageContent.Create measures it, so surrounding
        // whitespace cannot push an otherwise legal message over the limit.
        string padded = $"   {new string('a', PostMessageValidator.MaxRawInputLength)}   ";

        ValidationResult result = _validator.Validate(Command(padded));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyAuthorUserId_IsRejected(string? userId)
    {
        ValidationResult result = _validator.Validate(Command(userId: userId!));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(failure => failure.PropertyName == nameof(PostMessageCommand.AuthorUserId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyAuthorDisplayName_IsRejected(string? displayName)
    {
        // Checked here rather than only in the domain: the /stock= branch never builds a MessageAuthor, so
        // an empty identity would otherwise reach the broker inside a quote request.
        ValidationResult result = _validator.Validate(Command(displayName: displayName!));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(failure => failure.PropertyName == nameof(PostMessageCommand.AuthorDisplayName));
    }

    [Fact]
    public void MaxRawInputLength_MatchesTheDomainContentLimit()
    {
        PostMessageValidator.MaxRawInputLength.Should().Be(MessageConstants.MaxContentLength);
    }
}
