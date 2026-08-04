using Chat.Application.Features.Messages.GetLatestMessages;
using Chat.Domain.ChatRooms;
using Chat.Domain.Messages;
using FluentValidation.Results;

namespace Chat.UnitTests.Application.Features.Messages;

public sealed class GetLatestMessagesValidatorTests
{
    private static readonly ChatRoomId RoomId = ChatRoomId.New();

    private readonly GetLatestMessagesValidator _validator = new();

    [Fact]
    public void Validate_DefaultQuery_IsValid()
    {
        ValidationResult result = _validator.Validate(new GetLatestMessagesQuery(RoomId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DefaultRoomId_IsRejected()
    {
        ValidationResult result = _validator.Validate(new GetLatestMessagesQuery(default));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(GetLatestMessagesQuery.ChatRoomId));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    public void Validate_NonPositiveCount_IsRejected(int count)
    {
        ValidationResult result = _validator.Validate(new GetLatestMessagesQuery(RoomId, count));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(GetLatestMessagesQuery.Count));
    }

    [Theory]
    [InlineData(MessageConstants.LatestMessagesCount + 1)]
    [InlineData(10_000)]
    [InlineData(int.MaxValue)]
    public void Validate_CountAboveTheCap_IsRejected(int count)
    {
        // The cap is the point: Count reaches SQL as TOP(n), so an unbounded caller-supplied value would
        // let one client make the database read — and every chat window download — as much as it likes.
        ValidationResult result = _validator.Validate(new GetLatestMessagesQuery(RoomId, count));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(GetLatestMessagesQuery.Count));
    }

    [Theory]
    [InlineData(GetLatestMessagesValidator.MinimumCount)]
    [InlineData(MessageConstants.LatestMessagesCount)]
    public void Validate_CountOnTheBoundary_IsAccepted(int count)
    {
        ValidationResult result = _validator.Validate(new GetLatestMessagesQuery(RoomId, count));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DefaultCount_EqualsTheChallengeLimit()
    {
        // The mandatory "show only the last 50" requirement, pinned to the domain constant so nobody can
        // change the default without changing what the challenge asks for.
        new GetLatestMessagesQuery(RoomId).Count.Should().Be(MessageConstants.LatestMessagesCount);
    }
}
