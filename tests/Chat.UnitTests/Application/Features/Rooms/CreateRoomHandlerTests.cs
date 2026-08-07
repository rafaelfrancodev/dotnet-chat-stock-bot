using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Realtime;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Rooms;
using Chat.Application.Features.Rooms.CreateRoom;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using NSubstitute;

namespace Chat.UnitTests.Application.Features.Rooms;

/// <summary>
/// Creating a room is the multiple-chatrooms bonus's only write. What matters is that the name is
/// normalised before anything is decided about it, that a duplicate is an expected failure rather than a
/// database exception, that nothing is committed or announced on a failed path, and that the announcement
/// happens only after the commit.
/// </summary>
public sealed class CreateRoomHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 18, 45, 0, TimeSpan.Zero);

    private readonly IChatRoomRepository _chatRooms = Substitute.For<IChatRoomRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IChatRoomNotifier _notifier = Substitute.For<IChatRoomNotifier>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public CreateRoomHandlerTests() => _clock.UtcNow.Returns(Now);

    private CreateRoomHandler Handler => new(_chatRooms, _unitOfWork, _notifier, _clock);

    [Fact]
    public async Task Handle_NewName_CreatesTheRoomAndReturnsIt()
    {
        Result<ChatRoomDto> result = await Handler.Handle(new CreateRoomCommand("Trading"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Trading");
        result.Value.Id.Should().NotBe(Guid.Empty, "the caller joins the room by this id");

        _chatRooms.Received(1).Add(Arg.Is<ChatRoom>(room => room!.Name.Value == "Trading"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The returned identifier must be the one that was staged. Returning anything else would hand the
    /// caller a room id it could never join.
    /// </summary>
    [Fact]
    public async Task Handle_NewName_ReturnsTheIdentityOfTheRoomItStaged()
    {
        ChatRoom? staged = null;
        _chatRooms.When(rooms => rooms.Add(Arg.Any<ChatRoom>()))
            .Do(call => staged = call.Arg<ChatRoom>());

        Result<ChatRoomDto> result = await Handler.Handle(new CreateRoomCommand("Trading"), CancellationToken.None);

        staged.Should().NotBeNull();
        result.Value.Id.Should().Be(staged!.Id.Value);
    }

    /// <summary>
    /// The duplicate check has to run against the normalised name, or <c>"Trading  Desk"</c> would slip past
    /// a lookup for <c>"Trading Desk"</c> and be rejected by the unique index as an exception instead.
    /// </summary>
    [Theory]
    [InlineData("  Trading Desk  ", "Trading Desk")]
    [InlineData("Trading   Desk", "Trading Desk")]
    [InlineData("Trading\tDesk", "Trading Desk")]
    public async Task Handle_UnnormalisedName_ChecksAndStoresTheNormalisedForm(string typed, string expected)
    {
        await Handler.Handle(new CreateRoomCommand(typed), CancellationToken.None);

        await _chatRooms.Received(1).FindByNameAsync(
            Arg.Is<RoomName>(name => name != null && name.Value == expected),
            Arg.Any<CancellationToken>());

        _chatRooms.Received(1).Add(Arg.Is<ChatRoom>(room => room!.Name.Value == expected));
    }

    [Fact]
    public async Task Handle_NameAlreadyTaken_FailsAndWritesNothing()
    {
        _chatRooms.FindByNameAsync(Arg.Any<RoomName>(), Arg.Any<CancellationToken>())
            .Returns(new ChatRoomDto(Guid.CreateVersion7(), "Trading"));

        Result<ChatRoomDto> result = await Handler.Handle(new CreateRoomCommand("Trading"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ChatRoom.NameTaken");

        _chatRooms.DidNotReceive().Add(Arg.Any<ChatRoom>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().RoomCreatedAsync(Arg.Any<ChatRoomDto>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A name the value object refuses returns <see cref="RoomName"/>'s own error, unrestated: the domain
    /// owns the wording of its rules, and a second copy here would drift from it.
    /// </summary>
    [Theory]
    [InlineData("", "RoomName.Empty")]
    [InlineData("   ", "RoomName.Empty")]
    [InlineData("\t\n", "RoomName.Empty")]
    public async Task Handle_UnusableName_ReturnsTheDomainsOwnErrorAndWritesNothing(string name, string code)
    {
        Result<ChatRoomDto> result = await Handler.Handle(new CreateRoomCommand(name), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(code);

        _chatRooms.DidNotReceive().Add(Arg.Any<ChatRoom>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NameLongerThanTheLimit_FailsAndWritesNothing()
    {
        string tooLong = new('a', RoomName.MaxLength + 1);

        Result<ChatRoomDto> result = await Handler.Handle(new CreateRoomCommand(tooLong), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RoomName.TooLong");
        _chatRooms.DidNotReceive().Add(Arg.Any<ChatRoom>());
    }

    /// <summary>
    /// A name only over the limit because of duplicated whitespace is accepted, because
    /// <see cref="RoomName"/> collapses before it measures. Pinned here because it is exactly the rule the
    /// validator deliberately does not reproduce.
    /// </summary>
    [Fact]
    public async Task Handle_NameOverTheLimitOnlyBecauseOfRepeatedSpaces_IsAccepted()
    {
        string padded = string.Join("     ", Enumerable.Repeat("ab", 20));
        padded.Length.Should().BeGreaterThan(RoomName.MaxLength, "otherwise this test proves nothing");

        Result<ChatRoomDto> result = await Handler.Handle(new CreateRoomCommand(padded), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Length.Should().BeLessThanOrEqualTo(RoomName.MaxLength);
    }

    /// <summary>
    /// Announcing before the commit would offer other windows a room a failed save never stored — the same
    /// ordering rule <c>PostMessageHandler</c> follows for posts.
    /// </summary>
    [Fact]
    public async Task Handle_NewName_AnnouncesTheRoomOnlyAfterTheCommit()
    {
        bool committed = false;
        bool announcedBeforeCommit = false;

        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            committed = true;

            return 1;
        });

        _notifier.RoomCreatedAsync(Arg.Any<ChatRoomDto>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            announcedBeforeCommit = !committed;

            return Task.CompletedTask;
        });

        await Handler.Handle(new CreateRoomCommand("Trading"), CancellationToken.None);

        announcedBeforeCommit.Should().BeFalse();
        await _notifier.Received(1).RoomCreatedAsync(
            Arg.Is<ChatRoomDto>(room => room!.Name == "Trading"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NewName_StampsTheRoomFromTheApplicationClock()
    {
        ChatRoom? staged = null;
        _chatRooms.When(rooms => rooms.Add(Arg.Any<ChatRoom>()))
            .Do(call => staged = call.Arg<ChatRoom>());

        await Handler.Handle(new CreateRoomCommand("Trading"), CancellationToken.None);

        staged!.CreatedAtUtc.Should().Be(Now, "the domain never reads the ambient clock");
    }

    [Fact]
    public async Task Handle_Always_ForwardsTheCancellationToken()
    {
        using CancellationTokenSource cancellation = new();

        await Handler.Handle(new CreateRoomCommand("Trading"), cancellation.Token);

        await _chatRooms.Received(1).FindByNameAsync(Arg.Any<RoomName>(), cancellation.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
        await _notifier.Received(1).RoomCreatedAsync(Arg.Any<ChatRoomDto>(), cancellation.Token);
    }

    [Fact]
    public async Task Handle_NullCommand_Throws()
    {
        Func<Task> handling = () => Handler.Handle(null!, CancellationToken.None);

        await handling.Should().ThrowAsync<ArgumentNullException>();
    }
}
