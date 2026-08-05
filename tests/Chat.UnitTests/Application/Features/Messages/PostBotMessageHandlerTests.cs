using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Realtime;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Messages;
using Chat.Application.Contracts.Messaging;
using Chat.Application.Contracts.Realtime;
using Chat.Application.Errors;
using Chat.Application.Features.Messages.PostBotMessage;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Chat.UnitTests.Application.Features.Messages;

/// <summary>
/// The closing half of the stock round trip. Two things are worth pinning here: the answer really is owned
/// by the bot and really is committed before anybody is told about it, and the outage banner is aimed at
/// the one participant who asked — for the one outcome that means "the provider is down".
/// </summary>
public sealed class PostBotMessageHandlerTests
{
    private const string RequesterId = "user-1";
    private const string Answer = "AAPL.US quote is $93.42 per share";

    private static readonly ChatRoomId RoomId = ChatRoomId.New();
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 19, 15, 0, TimeSpan.Zero);

    private readonly IChatRoomRepository _chatRooms = Substitute.For<IChatRoomRepository>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IChatNotifier _notifier = Substitute.For<IChatNotifier>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public PostBotMessageHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _chatRooms.ExistsAsync(RoomId, Arg.Any<CancellationToken>()).Returns(true);
    }

    private PostBotMessageHandler CreateHandler() => new(
        _chatRooms,
        _messages,
        _unitOfWork,
        _notifier,
        _clock,
        NullLogger<PostBotMessageHandler>.Instance);

    private static PostBotMessageCommand Command(
        string text = Answer,
        StockQuoteOutcome outcome = StockQuoteOutcome.Quoted,
        string requestedByUserId = RequesterId) =>
        new(RoomId, text, requestedByUserId, outcome);

    [Fact]
    public async Task Handle_BotMessage_PersistsWithBotAuthorAndBroadcasts()
    {
        Message? posted = null;
        _messages.Add(Arg.Do<Message>(message => posted = message));

        Result result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        posted.Should().NotBeNull();
        posted!.ChatRoomId.Should().Be(RoomId);
        posted.PostedAtUtc.Should().Be(Now);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.Received(1).BroadcastMessageAsync(
            RoomId,
            Arg.Is<MessageDto>(message =>
                message!.Content == Answer
                && message.AuthorDisplayName == MessageAuthor.BotDisplayName
                && message.Origin == MessageOrigin.Bot),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// "The post owner should be the bot" is the challenge's wording, so it is asserted on the row that is
    /// written rather than only on what the browser renders.
    /// </summary>
    [Fact]
    public async Task Handle_BotMessage_IsOwnedByTheWellKnownBotAuthorAndNotByTheRequester()
    {
        Message? posted = null;
        _messages.Add(Arg.Do<Message>(message => posted = message));

        await CreateHandler().Handle(Command(), CancellationToken.None);

        posted!.Author.Should().BeSameAs(MessageAuthor.Bot);
        posted.Author.IsBot.Should().BeTrue();
        posted.Author.UserId.Should().Be(MessageAuthor.BotUserId).And.NotBe(RequesterId);
        posted.Origin.Should().Be(MessageOrigin.Bot);
    }

    /// <summary>
    /// The bot owns the wording (<c>StockQuoteAnswer</c>, unit-tested there), so Chat.Web must not
    /// re-format, prefix or append: the sentence the challenge grades has to exist in exactly one place.
    /// </summary>
    [Theory]
    [InlineData("AAPL.US quote is $93.42 per share")]
    [InlineData("Sorry, I could not find a quote for AAPL.XX.")]
    [InlineData("I could not reach the quote service, so I have no price for AAPL.US right now.")]
    public async Task Handle_Always_PostsTheBotsTextVerbatim(string text)
    {
        Message? posted = null;
        _messages.Add(Arg.Do<Message>(message => posted = message));

        await CreateHandler().Handle(Command(text), CancellationToken.None);

        posted!.Content.Value.Should().Be(text);
        await _notifier.Received(1).BroadcastMessageAsync(
            RoomId,
            Arg.Is<MessageDto>(message => message!.Content == text),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownRoom_ReturnsFailureWithoutBroadcast()
    {
        _chatRooms.ExistsAsync(RoomId, Arg.Any<CancellationToken>()).Returns(false);

        Result result = await CreateHandler().Handle(Command(), CancellationToken.None);

        // The shared failure promoted in task 1.9, not a third copy of "no such room".
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChatRoomErrors.NotFound);
        _messages.DidNotReceive().Add(Arg.Any<Message>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().BroadcastMessageAsync(default, default!, default);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyAlertAsync(default!, default!, default);
    }

    /// <summary>
    /// A provider outage is the one outcome that means "the system is degraded, retrying is worth it", so
    /// it gets a banner — aimed at the participant who typed the command and at nobody else.
    /// </summary>
    [Fact]
    public async Task Handle_LookupFailed_AlertsTheRequesterOnly()
    {
        Result result = await CreateHandler().Handle(
            Command(outcome: StockQuoteOutcome.LookupFailed),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _notifier.Received(1).NotifyAlertAsync(
            RequesterId,
            ChatAlert.QuoteServiceUnavailable,
            Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().NotifyAlertAsync(
            Arg.Is<string>(userId => userId != RequesterId),
            Arg.Any<ChatAlert>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LookupFailed_StillPostsTheAnswerToTheRoom()
    {
        // The banner complements the chat line; it does not replace it. A reviewer who dismisses the banner
        // must still be able to see what the bot answered.
        await CreateHandler().Handle(Command(outcome: StockQuoteOutcome.LookupFailed), CancellationToken.None);

        _messages.Received(1).Add(Arg.Any<Message>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.Received(1).BroadcastMessageAsync(RoomId, Arg.Any<MessageDto>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unknown ticker is a real answer from a working service. Claiming the provider is down would be
    /// false, and would train a reviewer to ignore the banner that does matter.
    /// </summary>
    [Fact]
    public async Task Handle_SymbolNotFound_RaisesNoAlert()
    {
        Result result = await CreateHandler().Handle(
            Command("Sorry, I could not find a quote for AAPL.XX.", StockQuoteOutcome.SymbolNotFound),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _messages.Received(1).Add(Arg.Any<Message>());
        await _notifier.DidNotReceiveWithAnyArgs().NotifyAlertAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_Quoted_RaisesNoAlert()
    {
        await CreateHandler().Handle(Command(), CancellationToken.None);

        await _notifier.DidNotReceiveWithAnyArgs().NotifyAlertAsync(default!, default!, default);
    }

    /// <summary>
    /// Only Chat.Bot publishes here, and it echoes an id taken from the caller's claims, so an empty
    /// requester means a hand-crafted payload. The room still gets the answer; there is simply nobody to
    /// aim a banner at, which must not turn into an exception from the notifier.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_LookupFailedWithoutARequester_PostsTheAnswerAndRaisesNoAlert(string requesterId)
    {
        Result result = await CreateHandler().Handle(
            Command(outcome: StockQuoteOutcome.LookupFailed, requestedByUserId: requesterId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _messages.Received(1).Add(Arg.Any<Message>());
        await _notifier.DidNotReceiveWithAnyArgs().NotifyAlertAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_BotMessage_CommitsBeforeBroadcasting()
    {
        // Announcing first would show connected browsers an answer a failed save never stored.
        await CreateHandler().Handle(Command(outcome: StockQuoteOutcome.LookupFailed), CancellationToken.None);

        Received.InOrder(() =>
        {
            _messages.Add(Arg.Any<Message>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _notifier.BroadcastMessageAsync(RoomId, Arg.Any<MessageDto>(), Arg.Any<CancellationToken>());
            _notifier.NotifyAlertAsync(RequesterId, ChatAlert.QuoteServiceUnavailable, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_SaveFails_DoesNotBroadcastOrAlert()
    {
        _unitOfWork
            .When(unitOfWork => unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("the database went away"));

        Func<Task> act = () => CreateHandler().Handle(
            Command(outcome: StockQuoteOutcome.LookupFailed),
            CancellationToken.None);

        // Propagated on purpose: nothing was written, so MassTransit retrying this delivery is exactly right.
        await act.Should().ThrowAsync<InvalidOperationException>();
        await _notifier.DidNotReceiveWithAnyArgs().BroadcastMessageAsync(default, default!, default);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyAlertAsync(default!, default!, default);
    }

    /// <summary>
    /// The one place idempotency is actually at risk: the post is committed and the fan-out then fails.
    /// Answering with a failed <see cref="Result"/> makes the consumer acknowledge the delivery, because a
    /// redelivery would insert the same answer a second time — permanently — while a missed push is
    /// recovered from the room history on the next join.
    /// </summary>
    [Fact]
    public async Task Handle_BroadcastFails_ReturnsAFailureInsteadOfAskingForARedelivery()
    {
        _notifier
            .BroadcastMessageAsync(RoomId, Arg.Any<MessageDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("the hub went away")));

        Result result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PostBotMessageCommand.Errors.NotAnnounced);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlertFails_ReturnsAFailureInsteadOfAskingForARedelivery()
    {
        _notifier
            .NotifyAlertAsync(RequesterId, Arg.Any<ChatAlert>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("the hub went away")));

        Result result = await CreateHandler().Handle(
            Command(outcome: StockQuoteOutcome.LookupFailed),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PostBotMessageCommand.Errors.NotAnnounced);
    }

    /// <summary>
    /// The host stopping is not a fan-out defect. The transport will redeliver whatever we return, so the
    /// cancellation propagates instead of being reported as a delivery that must not be retried.
    /// </summary>
    [Fact]
    public async Task Handle_HostStopsDuringTheBroadcast_PropagatesTheCancellation()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        _notifier
            .BroadcastMessageAsync(RoomId, Arg.Any<MessageDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new OperationCanceledException()));

        Func<Task> act = () => CreateHandler().Handle(Command(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Handle_TextTooLongForTheDomain_ReturnsFailureWithoutPersistingOrBroadcasting()
    {
        // The bot's own wording is short, but the column is bounded and this is a message from another
        // process: the domain rule applies to it exactly as it does to a participant's line.
        string tooLong = new('a', MessageConstants.MaxContentLength + 1);

        Result result = await CreateHandler().Handle(Command(tooLong), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MessageContent.Errors.TooLong);
        _messages.DidNotReceive().Add(Arg.Any<Message>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().BroadcastMessageAsync(default, default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_EmptyText_ReturnsFailureWithoutPersisting(string text)
    {
        Result result = await CreateHandler().Handle(Command(text), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MessageContent.Errors.Empty);
        _messages.DidNotReceive().Add(Arg.Any<Message>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DomainFactoryFails_DoesNotPersistOrBroadcast()
    {
        // A default post time is refused by Message.PostByBot, which must stop the write outright.
        _clock.UtcNow.Returns(default(DateTimeOffset));

        Result result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Message.Errors.MissingPostTime);
        _messages.DidNotReceive().Add(Arg.Any<Message>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().BroadcastMessageAsync(default, default!, default);
    }

    [Fact]
    public async Task Handle_Always_ForwardsTheCancellationTokenToEveryCall()
    {
        using CancellationTokenSource cancellation = new();

        await CreateHandler().Handle(Command(outcome: StockQuoteOutcome.LookupFailed), cancellation.Token);

        await _chatRooms.Received(1).ExistsAsync(RoomId, cancellation.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
        await _notifier.Received(1).BroadcastMessageAsync(RoomId, Arg.Any<MessageDto>(), cancellation.Token);
        await _notifier.Received(1).NotifyAlertAsync(
            RequesterId,
            ChatAlert.QuoteServiceUnavailable,
            cancellation.Token);
    }

    [Fact]
    public async Task Handle_NullCommand_Throws()
    {
        Func<Task> act = () => CreateHandler().Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Handler_IsMarkedAsAWebFeature_SoChatWebRegistersIt()
    {
        // Without the marker no host registers it, and the bot's answer would fail with "no handler for
        // request" at the far end of the round trip instead of at build time.
        typeof(PostBotMessageHandler).Should().BeAssignableTo<IWebFeature>();
    }
}
