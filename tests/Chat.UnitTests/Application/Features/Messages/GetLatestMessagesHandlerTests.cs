using Chat.Application.Abstractions.Persistence;
using Chat.Application.Contracts.Messages;
using Chat.Application.Features.Messages.GetLatestMessages;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.Messages;
using NSubstitute;

namespace Chat.UnitTests.Application.Features.Messages;

public sealed class GetLatestMessagesHandlerTests
{
    private static readonly ChatRoomId RoomId = ChatRoomId.New();
    private static readonly DateTimeOffset FirstPostedAt = new(2026, 8, 4, 10, 0, 0, TimeSpan.Zero);

    private readonly IChatRoomRepository _chatRooms = Substitute.For<IChatRoomRepository>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();

    private GetLatestMessagesHandler CreateHandler() => new(_chatRooms, _messages);

    [Fact]
    public async Task Handle_RoomWithMessages_ReturnsOldestToNewest()
    {
        RoomExists();
        IReadOnlyList<MessageDto> history = HistoryOf(3);
        ReturnsHistory(history);

        Result<IReadOnlyList<MessageDto>> result =
            await CreateHandler().Handle(new GetLatestMessagesQuery(RoomId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Select(message => message.Content).Should().ContainInOrder("message 1", "message 2", "message 3");
        result.Value.Should().BeInAscendingOrder(message => message.PostedAtUtc);
    }

    [Fact]
    public async Task Handle_RoomWithMessages_ReturnsTheRepositorySequenceUntouched()
    {
        RoomExists();
        IReadOnlyList<MessageDto> history = HistoryOf(3);
        ReturnsHistory(history);

        Result<IReadOnlyList<MessageDto>> result =
            await CreateHandler().Handle(new GetLatestMessagesQuery(RoomId), CancellationToken.None);

        // The database applied the filter, the ordering and the limit: re-sorting or copying here would
        // duplicate work and hide it. Same instance is the cheapest proof that neither happened.
        result.Value.Should().BeSameAs(history);
    }

    [Fact]
    public async Task Handle_MoreThan50Messages_ReturnsOnly50()
    {
        RoomExists();
        ReturnsNewestOf(HistoryOf(120));

        Result<IReadOnlyList<MessageDto>> result =
            await CreateHandler().Handle(new GetLatestMessagesQuery(RoomId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(MessageConstants.LatestMessagesCount);
        result.Value[0].Content.Should().Be("message 71");
        result.Value[^1].Content.Should().Be("message 120");
    }

    [Fact]
    public async Task Handle_UnknownRoom_ReturnsFailure()
    {
        _chatRooms.ExistsAsync(RoomId, Arg.Any<CancellationToken>()).Returns(false);

        Result<IReadOnlyList<MessageDto>> result =
            await CreateHandler().Handle(new GetLatestMessagesQuery(RoomId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(GetLatestMessagesQuery.Errors.ChatRoomNotFound);
    }

    [Fact]
    public async Task Handle_UnknownRoom_DoesNotQueryMessages()
    {
        _chatRooms.ExistsAsync(RoomId, Arg.Any<CancellationToken>()).Returns(false);

        await CreateHandler().Handle(new GetLatestMessagesQuery(RoomId), CancellationToken.None);

        await _messages.DidNotReceiveWithAnyArgs().GetLatestAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_NoCountSupplied_RequestsTheDefaultCountFromTheRepository()
    {
        RoomExists();
        ReturnsHistory([]);

        await CreateHandler().Handle(new GetLatestMessagesQuery(RoomId), CancellationToken.None);

        await _messages.Received(1).GetLatestAsync(
            RoomId,
            MessageConstants.LatestMessagesCount,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExplicitCount_IsPassedThroughToTheRepository()
    {
        RoomExists();
        ReturnsHistory([]);

        await CreateHandler().Handle(new GetLatestMessagesQuery(RoomId, 10), CancellationToken.None);

        await _messages.Received(1).GetLatestAsync(RoomId, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Always_ForwardsTheCancellationTokenToBothQueries()
    {
        using CancellationTokenSource cancellation = new();
        RoomExists();
        ReturnsHistory([]);

        await CreateHandler().Handle(new GetLatestMessagesQuery(RoomId), cancellation.Token);

        await _chatRooms.Received(1).ExistsAsync(RoomId, cancellation.Token);
        await _messages.Received(1).GetLatestAsync(RoomId, Arg.Any<int>(), cancellation.Token);
    }

    [Fact]
    public async Task Handle_EmptyRoom_SucceedsWithAnEmptyList()
    {
        RoomExists();
        ReturnsHistory([]);

        Result<IReadOnlyList<MessageDto>> result =
            await CreateHandler().Handle(new GetLatestMessagesQuery(RoomId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    private void RoomExists() => _chatRooms.ExistsAsync(RoomId, Arg.Any<CancellationToken>()).Returns(true);

    private void ReturnsHistory(IReadOnlyList<MessageDto> history) =>
        _messages.GetLatestAsync(RoomId, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(history);

    /// <summary>
    /// Emulates the repository contract: the newest <c>count</c> posts, handed back oldest to newest.
    /// The cap belongs to the query, so the substitute has to honour it for the test to mean anything.
    /// </summary>
    private void ReturnsNewestOf(IReadOnlyList<MessageDto> fullHistory) =>
        _messages.GetLatestAsync(RoomId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                IReadOnlyList<MessageDto> newest = [.. fullHistory.TakeLast(call.ArgAt<int>(1))];
                return newest;
            });

    private static IReadOnlyList<MessageDto> HistoryOf(int count) =>
        [.. Enumerable.Range(1, count).Select(number => new MessageDto(
            Guid.CreateVersion7(),
            "Alice",
            $"message {number}",
            FirstPostedAt.AddMinutes(number),
            MessageOrigin.Participant))];
}
