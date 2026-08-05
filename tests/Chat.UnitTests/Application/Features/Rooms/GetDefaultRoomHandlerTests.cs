using Chat.Application.Abstractions.Persistence;
using Chat.Application.Contracts.Rooms;
using Chat.Application.Features.Rooms.GetDefaultRoom;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using NSubstitute;

namespace Chat.UnitTests.Application.Features.Rooms;

/// <summary>
/// The chat page needs a room identifier before it can join anything. This query is how it gets one
/// without reaching past the Application layer, so what matters is that it asks for the room the seeder
/// creates, hands the projection back untouched, and treats an unseeded database as an expected failure.
/// </summary>
public sealed class GetDefaultRoomHandlerTests
{
    private static readonly ChatRoomDto SeededRoom = new(Guid.CreateVersion7(), ChatRoomConstants.DefaultRoomName);

    private readonly IChatRoomRepository _chatRooms = Substitute.For<IChatRoomRepository>();

    private GetDefaultRoomHandler Handler => new(_chatRooms);

    [Fact]
    public async Task Handle_SeededRoom_ReturnsItUntouched()
    {
        _chatRooms.FindByNameAsync(Arg.Any<RoomName>(), Arg.Any<CancellationToken>()).Returns(SeededRoom);

        Result<ChatRoomDto> result = await Handler.Handle(new GetDefaultRoomQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(SeededRoom, "the handler projects nothing of its own");
    }

    [Fact]
    public async Task Handle_Always_LooksUpTheNameTheSeederCreates()
    {
        _chatRooms.FindByNameAsync(Arg.Any<RoomName>(), Arg.Any<CancellationToken>()).Returns(SeededRoom);

        await Handler.Handle(new GetDefaultRoomQuery(), CancellationToken.None);

        await _chatRooms.Received(1).FindByNameAsync(
            Arg.Is<RoomName>(name => name != null && name.Value == ChatRoomConstants.DefaultRoomName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnseededDatabase_ReturnsTheSharedNotFoundFailure()
    {
        _chatRooms.FindByNameAsync(Arg.Any<RoomName>(), Arg.Any<CancellationToken>())
            .Returns((ChatRoomDto?)null);

        Result<ChatRoomDto> result = await Handler.Handle(new GetDefaultRoomQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ChatRoom.NotFound", "the code a client sees must not be a third copy");
    }

    [Fact]
    public async Task Handle_Always_ForwardsTheCancellationToken()
    {
        using CancellationTokenSource cancellation = new();
        _chatRooms.FindByNameAsync(Arg.Any<RoomName>(), Arg.Any<CancellationToken>()).Returns(SeededRoom);

        await Handler.Handle(new GetDefaultRoomQuery(), cancellation.Token);

        await _chatRooms.Received(1).FindByNameAsync(Arg.Any<RoomName>(), cancellation.Token);
    }

    [Fact]
    public async Task Handle_NullQuery_Throws()
    {
        Func<Task> handling = () => Handler.Handle(null!, CancellationToken.None);

        await handling.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void DefaultRoomName_IsAValidRoomName()
    {
        // The handler validates the constant once in a static initialiser; if the room-name rules ever
        // stopped accepting it, every chat page would fail with a type-initialisation error instead.
        RoomName.Create(ChatRoomConstants.DefaultRoomName).IsSuccess.Should().BeTrue();
    }
}
