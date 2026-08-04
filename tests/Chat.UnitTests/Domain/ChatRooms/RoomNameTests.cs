using Chat.Domain.ChatRooms;
using Chat.Domain.Common;

namespace Chat.UnitTests.Domain.ChatRooms;

public sealed class RoomNameTests
{
    [Fact]
    public void Create_EmptyName_ReturnsFailure()
    {
        Result<RoomName> result = RoomName.Create(string.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RoomName.Errors.Empty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\t\n  ")]
    public void Create_WhitespaceOnly_ReturnsFailure(string? value)
    {
        Result<RoomName> result = RoomName.Create(value);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RoomName.Errors.Empty);
    }

    [Fact]
    public void Create_TooLong_ReturnsFailure()
    {
        string tooLong = new('a', RoomName.MaxLength + 1);

        Result<RoomName> result = RoomName.Create(tooLong);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RoomName.Errors.TooLong);
    }

    [Fact]
    public void Create_ExactlyMaxLength_Succeeds()
    {
        string atLimit = new('a', RoomName.MaxLength);

        Result<RoomName> result = RoomName.Create(atLimit);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().HaveLength(RoomName.MaxLength);
    }

    [Fact]
    public void Create_LengthExceededOnlyByCollapsibleWhitespace_NormalisesAndSucceeds()
    {
        // 62 characters as typed, exactly 60 once the run of spaces is collapsed — the limit applies
        // to the normalised name, so this must succeed.
        string padded = $"{new string('a', RoomName.MaxLength - 2)}   b";

        Result<RoomName> result = RoomName.Create(padded);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().HaveLength(RoomName.MaxLength);
    }

    [Theory]
    [InlineData("General   Chat", "General Chat")]
    [InlineData("General\tChat", "General Chat")]
    [InlineData("General \t\n Chat", "General Chat")]
    [InlineData("Trading  Floor   Room", "Trading Floor Room")]
    [InlineData("General Chat", "General Chat")]
    public void Create_InternalWhitespace_CollapsesToSingleSpaces(string value, string expected)
    {
        Result<RoomName> result = RoomName.Create(value);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
    }

    [Fact]
    public void Create_NonBreakingSpace_IsCollapsedLikeAnyOtherWhitespace()
    {
        // Escaped on purpose: a raw U+00A0 in the source would be invisible to a reader.
        Result<RoomName> result = RoomName.Create("\u00A0General\u00A0\u00A0Chat\u00A0");

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be("General Chat");
    }

    [Fact]
    public void Create_SurroundingWhitespace_TrimsAndSucceeds()
    {
        Result<RoomName> result = RoomName.Create("  \tGeneral\n ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be("General");
    }

    [Fact]
    public void Equality_DifferentlySpacedInput_AreEqual()
    {
        RoomName first = RoomName.Create("General Chat").Value;
        RoomName second = RoomName.Create("  General \t  Chat  ").Value;

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        RoomName first = RoomName.Create("General").Value;
        RoomName second = RoomName.Create("Trading").Value;

        first.Should().NotBe(second);
    }

    [Fact]
    public void Equality_DifferentCasing_AreNotEqual()
    {
        RoomName first = RoomName.Create("General").Value;
        RoomName second = RoomName.Create("general").Value;

        first.Should().NotBe(second);
    }

    [Fact]
    public void ToString_ValidName_ReturnsNormalisedText()
    {
        RoomName name = RoomName.Create(" General   Chat ").Value;

        name.ToString().Should().Be("General Chat");
    }
}
