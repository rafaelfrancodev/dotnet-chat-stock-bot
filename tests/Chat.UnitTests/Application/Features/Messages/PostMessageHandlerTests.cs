using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Realtime;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Messages;
using Chat.Application.Errors;
using Chat.Application.Features.Messages.PostMessage;
using Chat.Application.Features.StockCommands.RequestStockQuote;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.Messages;
using Chat.Domain.StockCommands;
using MediatR;
using NSubstitute;

namespace Chat.UnitTests.Application.Features.Messages;

/// <summary>
/// The challenge's hard constraint lives in this handler: a <c>/stock=</c> command is never saved as a
/// post. The tests below prove it from both sides — the repository and the unit of work are never
/// touched on that branch, and the request does reach the bot.
/// </summary>
public sealed class PostMessageHandlerTests
{
    private const string UserId = "user-1";
    private const string DisplayName = "Alice";

    /// <summary>21 characters — one over <c>StockCode.MaxLength</c>, spelled out so the length is obvious.</summary>
    private const string OverlongStockCommand = "/stock=" + "aaaaaaaaaa" + "aaaaaaaaaa" + "a";

    private static readonly ChatRoomId RoomId = ChatRoomId.New();
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 30, 0, TimeSpan.Zero);

    private readonly IChatRoomRepository _chatRooms = Substitute.For<IChatRoomRepository>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IChatNotifier _notifier = Substitute.For<IChatNotifier>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ISender _sender = Substitute.For<ISender>();

    public PostMessageHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _chatRooms.ExistsAsync(RoomId, Arg.Any<CancellationToken>()).Returns(true);
        _sender.Send(Arg.Any<RequestStockQuoteCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    private PostMessageHandler CreateHandler() =>
        new(_chatRooms, _messages, _unitOfWork, _notifier, _clock, _sender);

    private static PostMessageCommand Command(string rawInput) =>
        new(RoomId, rawInput, UserId, DisplayName);

    [Fact]
    public async Task Handle_PlainMessage_PersistsAndNotifies()
    {
        Result<PostMessageOutcome> result =
            await CreateHandler().Handle(Command("hello team"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(PostMessageOutcome.Posted);
        _messages.Received(1).Add(Arg.Any<Message>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.Received(1).BroadcastMessageAsync(
            RoomId,
            Arg.Is<MessageDto>(message =>
                message!.Content == "hello team"
                && message.AuthorDisplayName == DisplayName
                && message.Origin == MessageOrigin.Participant),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PlainMessage_PersistsTheClaimsAuthorTheRoomAndTheClockInstant()
    {
        Message? posted = null;
        _messages.Add(Arg.Do<Message>(message => posted = message));

        await CreateHandler().Handle(Command("  hello team  "), CancellationToken.None);

        posted.Should().NotBeNull();
        posted!.ChatRoomId.Should().Be(RoomId);
        posted.Author.UserId.Should().Be(UserId);
        posted.Author.DisplayName.Should().Be(DisplayName);
        posted.Author.IsBot.Should().BeFalse();
        posted.Content.Value.Should().Be("hello team");
        posted.PostedAtUtc.Should().Be(Now);
        posted.Origin.Should().Be(MessageOrigin.Participant);
    }

    [Fact]
    public async Task Handle_PlainMessage_CommitsBeforeBroadcasting()
    {
        // Announcing first would show connected browsers a post a failed save never stored.
        await CreateHandler().Handle(Command("hello team"), CancellationToken.None);

        Received.InOrder(() =>
        {
            _messages.Add(Arg.Any<Message>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _notifier.BroadcastMessageAsync(RoomId, Arg.Any<MessageDto>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_PlainMessage_DoesNotPublishAStockRequest()
    {
        await CreateHandler().Handle(Command("hello team"), CancellationToken.None);

        await _sender.DidNotReceive().Send(Arg.Any<RequestStockQuoteCommand>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("/stock=aapl.us")]
    [InlineData("/STOCK=AAPL.US")]
    [InlineData("   /stock=aapl.us   ")]
    public async Task Handle_StockCommand_DoesNotPersistMessage(string rawInput)
    {
        // The hard challenge constraint. Both halves matter: nothing is staged, and nothing is committed.
        Result<PostMessageOutcome> result =
            await CreateHandler().Handle(Command(rawInput), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(PostMessageOutcome.QuoteRequested);
        _messages.DidNotReceive().Add(Arg.Any<Message>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StockCommand_DoesNotBroadcastTheCommand()
    {
        // The command is not a post, so it must not appear in anybody's chat window either.
        await CreateHandler().Handle(Command("/stock=aapl.us"), CancellationToken.None);

        await _notifier.DidNotReceiveWithAnyArgs().BroadcastMessageAsync(default, default!, default);
    }

    [Fact]
    public async Task Handle_StockCommand_PublishesStockQuoteRequest()
    {
        await CreateHandler().Handle(Command("/STOCK=AAPL.US"), CancellationToken.None);

        await _sender.Received(1).Send(
            Arg.Is<RequestStockQuoteCommand>(command =>
                command!.ChatRoomId == RoomId
                && command.StockCode.Value == "aapl.us"
                && command.RequestedByUserId == UserId
                && command.RequestedByDisplayName == DisplayName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StockCommandDispatchFails_ReturnsThatFailureAndPersistsNothing()
    {
        Error brokerDown = Error.Failure("Broker.Unavailable", "The broker is unavailable.");
        _sender.Send(Arg.Any<RequestStockQuoteCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(brokerDown));

        Result<PostMessageOutcome> result =
            await CreateHandler().Handle(Command("/stock=aapl.us"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(brokerDown);
        _messages.DidNotReceive().Add(Arg.Any<Message>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("/help")]
    [InlineData("/HELP")]
    [InlineData("/quit please")]
    public async Task Handle_UnknownCommand_ReturnsFailureAndPersistsNothing(string rawInput)
    {
        Result<PostMessageOutcome> result =
            await CreateHandler().Handle(Command(rawInput), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PostMessageCommand.Errors.UnknownCommand);
        _messages.DidNotReceive().Add(Arg.Any<Message>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _sender.DidNotReceive().Send(Arg.Any<RequestStockQuoteCommand>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().BroadcastMessageAsync(default, default!, default);
    }

    [Fact]
    public async Task Handle_UnknownCommand_DoesNotEchoTheCommandNameBackToTheCaller()
    {
        // The command name is untrusted and unbounded, so it never becomes part of an error message.
        string hostile = new('x', 5_000);

        Result<PostMessageOutcome> result =
            await CreateHandler().Handle(Command($"/{hostile}"), CancellationToken.None);

        result.Error.Message.Should().NotContain(hostile);
    }

    [Theory]
    [InlineData("/stock=", "StockCode.Empty")]
    [InlineData("/stock=a&b=c", "StockCode.InvalidFormat")]
    [InlineData(OverlongStockCommand, "StockCode.TooLong")]
    [InlineData("/=aapl.us", "ChatCommand.MissingCommandName")]
    public async Task Handle_InvalidCommand_ReturnsTheParserErrorAndPersistsNothing(string rawInput, string errorCode)
    {
        Result<PostMessageOutcome> result =
            await CreateHandler().Handle(Command(rawInput), CancellationToken.None);

        // The parser's own error, unchanged: the ticker rules belong to StockCode and are not restated.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(errorCode);
        _messages.DidNotReceive().Add(Arg.Any<Message>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _sender.DidNotReceive().Send(Arg.Any<RequestStockQuoteCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidStockCommand_ReturnsTheStockCodeErrorInstance()
    {
        Result<PostMessageOutcome> result =
            await CreateHandler().Handle(Command("/stock="), CancellationToken.None);

        result.Error.Should().Be(StockCode.Errors.Empty);
    }

    [Fact]
    public async Task Handle_UnknownRoom_ReturnsFailure()
    {
        _chatRooms.ExistsAsync(RoomId, Arg.Any<CancellationToken>()).Returns(false);

        Result<PostMessageOutcome> result =
            await CreateHandler().Handle(Command("hello team"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChatRoomErrors.NotFound);
    }

    [Theory]
    [InlineData("hello team")]
    [InlineData("/stock=aapl.us")]
    [InlineData("/help")]
    public async Task Handle_UnknownRoom_DoesNoWorkOnAnyBranch(string rawInput)
    {
        // The room check short-circuits before the input is classified, so no branch can spend anything.
        _chatRooms.ExistsAsync(RoomId, Arg.Any<CancellationToken>()).Returns(false);

        await CreateHandler().Handle(Command(rawInput), CancellationToken.None);

        _messages.DidNotReceive().Add(Arg.Any<Message>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _sender.DidNotReceive().Send(Arg.Any<RequestStockQuoteCommand>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().BroadcastMessageAsync(default, default!, default);
    }

    [Fact]
    public async Task Handle_SaveFails_DoesNotNotify()
    {
        _unitOfWork
            .When(unitOfWork => unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("the database went away"));

        Func<Task> act = () => CreateHandler().Handle(Command("hello team"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _notifier.DidNotReceiveWithAnyArgs().BroadcastMessageAsync(default, default!, default);
    }

    [Theory]
    [InlineData("", "", "MessageAuthor.EmptyUserId")]
    [InlineData(UserId, "   ", "MessageAuthor.EmptyDisplayName")]
    public async Task Handle_UnusableAuthor_ReturnsFailureWithoutPersistingOrNotifying(
        string userId,
        string displayName,
        string errorCode)
    {
        PostMessageCommand command = new(RoomId, "hello team", userId, displayName);

        Result<PostMessageOutcome> result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(errorCode);
        _messages.DidNotReceive().Add(Arg.Any<Message>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().BroadcastMessageAsync(default, default!, default);
    }

    [Fact]
    public async Task Handle_ContentTooLongForTheDomain_ReturnsFailureWithoutPersistingOrNotifying()
    {
        string tooLong = new('a', MessageConstants.MaxContentLength + 1);

        Result<PostMessageOutcome> result = await CreateHandler().Handle(Command(tooLong), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MessageContent.Errors.TooLong);
        _messages.DidNotReceive().Add(Arg.Any<Message>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().BroadcastMessageAsync(default, default!, default);
    }

    [Fact]
    public async Task Handle_DomainFactoryFails_DoesNotNotify()
    {
        // A default post time is refused by Message.PostByParticipant, which must stop the write outright.
        _clock.UtcNow.Returns(default(DateTimeOffset));

        Result<PostMessageOutcome> result =
            await CreateHandler().Handle(Command("hello team"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Message.Errors.MissingPostTime);
        _messages.DidNotReceive().Add(Arg.Any<Message>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().BroadcastMessageAsync(default, default!, default);
    }

    [Fact]
    public async Task Handle_PlainMessage_ForwardsTheCancellationTokenToEveryCall()
    {
        using CancellationTokenSource cancellation = new();

        await CreateHandler().Handle(Command("hello team"), cancellation.Token);

        await _chatRooms.Received(1).ExistsAsync(RoomId, cancellation.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
        await _notifier.Received(1).BroadcastMessageAsync(RoomId, Arg.Any<MessageDto>(), cancellation.Token);
    }

    [Fact]
    public async Task Handle_StockCommand_ForwardsTheCancellationTokenToTheDispatch()
    {
        using CancellationTokenSource cancellation = new();

        await CreateHandler().Handle(Command("/stock=aapl.us"), cancellation.Token);

        await _chatRooms.Received(1).ExistsAsync(RoomId, cancellation.Token);
        await _sender.Received(1).Send(Arg.Any<RequestStockQuoteCommand>(), cancellation.Token);
    }

    [Fact]
    public async Task Handle_NullCommand_Throws()
    {
        Func<Task> act = () => CreateHandler().Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
