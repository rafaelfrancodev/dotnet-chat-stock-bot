using Chat.Application.Abstractions.Persistence;
using Chat.Application.Contracts.Rooms;
using Chat.Application.Features.Rooms.ListRooms;
using Chat.Domain.Common;
using NSubstitute;

namespace Chat.UnitTests.Application.Features.Rooms;

/// <summary>
/// The room directory the picker renders. The handler is a pass-through, so what is worth pinning is that
/// it stays one: no re-ordering, no filtering, and an empty directory is a success rather than a failure.
/// </summary>
public sealed class ListRoomsHandlerTests
{
    private static readonly ChatRoomDto General = new(Guid.CreateVersion7(), "General");
    private static readonly ChatRoomDto Trading = new(Guid.CreateVersion7(), "Trading");

    private readonly IChatRoomRepository _chatRooms = Substitute.For<IChatRoomRepository>();

    private ListRoomsHandler Handler => new(_chatRooms);

    /// <summary>
    /// Ordering belongs to the repository, which sorts in SQL. Re-sorting here would be a second ordering
    /// to keep in step with the first.
    /// </summary>
    [Fact]
    public async Task Handle_Rooms_ReturnsThemInTheRepositorysOrder()
    {
        _chatRooms.ListAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ChatRoomDto>>([General, Trading]);

        Result<IReadOnlyList<ChatRoomDto>> result = await Handler.Handle(new ListRoomsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(General, Trading);
    }

    /// <summary>
    /// An empty directory means seeding has not run. The page explains that; it is not an error the
    /// pipeline should turn into a failed result.
    /// </summary>
    [Fact]
    public async Task Handle_NoRooms_SucceedsWithAnEmptyList()
    {
        _chatRooms.ListAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<ChatRoomDto>>([]);

        Result<IReadOnlyList<ChatRoomDto>> result = await Handler.Handle(new ListRoomsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Always_ForwardsTheCancellationToken()
    {
        using CancellationTokenSource cancellation = new();
        _chatRooms.ListAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<ChatRoomDto>>([]);

        await Handler.Handle(new ListRoomsQuery(), cancellation.Token);

        await _chatRooms.Received(1).ListAsync(cancellation.Token);
    }

    [Fact]
    public async Task Handle_NullQuery_Throws()
    {
        Func<Task> handling = () => Handler.Handle(null!, CancellationToken.None);

        await handling.Should().ThrowAsync<ArgumentNullException>();
    }
}
