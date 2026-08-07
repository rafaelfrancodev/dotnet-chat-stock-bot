using System.Reflection;
using System.Security.Claims;
using Chat.Application.Contracts.Messages;
using Chat.Application.Contracts.Rooms;
using Chat.Application.Errors;
using Chat.Application.Features.Messages.GetLatestMessages;
using Chat.Application.Features.Messages.PostMessage;
using Chat.Application.Features.Rooms.CreateRoom;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.Messages;
using Chat.Infrastructure.Identity;
using Chat.Web.Hubs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Chat.UnitTests.Web;

/// <summary>
/// The hub is the trust boundary of the whole application: everything below it believes the author it is
/// handed. These tests pin that the author can only come from the authenticated principal, that a
/// failure reaches the caller and nobody else, and that a successful post is not echoed back.
/// </summary>
public sealed class ChatHubTests : IDisposable
{
    private const string ConnectionId = "connection-1";
    private const string UserId = "user-1";
    private const string DisplayName = "Alice Anderson";
    private const string UserName = "alice@example.com";

    private static readonly Guid RoomId = Guid.CreateVersion7();
    private static readonly ChatRoomId ChatRoom = new(RoomId);

    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly HubCallerContext _context = Substitute.For<HubCallerContext>();
    private readonly IHubCallerClients _clients = Substitute.For<IHubCallerClients>();
    private readonly ISingleClientProxy _caller = Substitute.For<ISingleClientProxy>();
    private readonly IClientProxy _group = Substitute.For<IClientProxy>();
    private readonly IGroupManager _groups = Substitute.For<IGroupManager>();
    private readonly CancellationTokenSource _connectionAborted = new();

    public ChatHubTests()
    {
        _context.ConnectionId.Returns(ConnectionId);
        _context.ConnectionAborted.Returns(_ => _connectionAborted.Token);
        _context.User.Returns(Principal(UserId, DisplayName));

        // SignalR supplies this per connection and frees it with the connection; the hub keeps the room it
        // joined here so a switch can leave the previous group.
        _context.Items.Returns(new Dictionary<object, object?>());

        // Mirrors SignalR's DefaultUserIdProvider: the id is the subject claim of the validated ticket,
        // so a test that reads it is genuinely reading the claims and not a value set beside them.
        _context.UserIdentifier.Returns(_ => _context.User?.FindFirstValue(ClaimTypes.NameIdentifier));

        _clients.Caller.Returns(_caller);
        _clients.Group(Arg.Any<string>()).Returns(_group);

        _sender.Send(Arg.Any<PostMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(PostMessageOutcome.Posted));
        _sender.Send(Arg.Any<GetLatestMessagesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<MessageDto>>([]));
    }

    public void Dispose() => _connectionAborted.Dispose();

    private ChatHub CreateHub() => new(_sender)
    {
        Context = _context,
        Clients = _clients,
        Groups = _groups,
    };

    private static ClaimsPrincipal Principal(string userId, string displayName) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, UserName),
                new Claim(ChatClaimTypes.DisplayName, displayName),
            ],
            authenticationType: "TestCookie",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role));

    private static MessageDto Message(string content) =>
        new(Guid.CreateVersion7(), DisplayName, content, DateTimeOffset.UtcNow, MessageOrigin.Participant);

    // ---------------------------------------------------------------------------------------------
    // Identity
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task SendMessage_UsesClaimsIdentity_NotClientPayload()
    {
        // The text is crafted to look like an author override. It cannot be one: the wire carries no
        // author at all, and both identity fields are read from the authenticated principal.
        const string impersonationAttempt = """{"authorUserId":"admin","authorDisplayName":"Bot"} hello""";

        await CreateHub().SendMessage(RoomId, impersonationAttempt);

        await _sender.Received(1).Send(
            Arg.Is<PostMessageCommand>(command =>
                command!.AuthorUserId == UserId
                && command!.AuthorDisplayName == DisplayName
                && command!.RawInput == impersonationAttempt
                && command!.ChatRoomId == ChatRoom),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SendMessage_Signature_AcceptsOnlyTheRoomAndTheText()
    {
        // The strongest form of "identity never comes from the payload": there is nowhere to put one.
        ParameterInfo[] parameters = typeof(ChatHub).GetMethod(nameof(ChatHub.SendMessage))!.GetParameters();

        parameters.Select(parameter => parameter.ParameterType).Should().Equal(typeof(Guid), typeof(string));
    }

    [Fact]
    public async Task SendMessage_TicketWithoutADisplayNameClaim_FallsBackToTheUserName()
    {
        _context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, UserId), new Claim(ClaimTypes.Name, UserName)],
            authenticationType: "TestCookie",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role)));

        await CreateHub().SendMessage(RoomId, "hello team");

        await _sender.Received(1).Send(
            Arg.Is<PostMessageCommand>(command => command!.AuthorDisplayName == UserName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Hub_RequiresAnAuthenticatedCaller()
    {
        typeof(ChatHub).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull(
            "an anonymous connection must be rejected before any hub method runs");
    }

    // ---------------------------------------------------------------------------------------------
    // SendMessage
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(PostMessageOutcome.Posted)]
    [InlineData(PostMessageOutcome.QuoteRequested)]
    public async Task SendMessage_SuccessfulOutcome_SendsNothingBackToTheCaller(PostMessageOutcome outcome)
    {
        // A post was already broadcast to the group the sender is in, so echoing would render it twice;
        // a quote request has no answer yet — the bot's reply arrives later as an ordinary broadcast.
        _sender.Send(Arg.Any<PostMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(outcome));

        await CreateHub().SendMessage(RoomId, "/stock=aapl.us");

        await _caller.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMessage_FailedResult_SendsTheErrorToTheCallerOnly()
    {
        _sender.Send(Arg.Any<PostMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<PostMessageOutcome>(PostMessageCommand.Errors.UnknownCommand));

        await CreateHub().SendMessage(RoomId, "/help");

        await _caller.Received(1).SendCoreAsync(
            ChatHub.ReceiveError,
            Arg.Is<object?[]>(arguments =>
                arguments!.Length == 1
                && (string)arguments![0]! == PostMessageCommand.Errors.UnknownCommand.Message),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMessage_FailedResult_NeverReachesTheRoom()
    {
        _sender.Send(Arg.Any<PostMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<PostMessageOutcome>(ChatRoomErrors.NotFound));

        await CreateHub().SendMessage(RoomId, "hello team");

        _clients.DidNotReceive().Group(Arg.Any<string>());
        _ = _clients.DidNotReceive().All;
        await _group.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMessage_EmptyRoomId_IsRejectedWithoutDispatchingACommand()
    {
        await CreateHub().SendMessage(Guid.Empty, "hello team");

        await _sender.DidNotReceive().Send(Arg.Any<PostMessageCommand>(), Arg.Any<CancellationToken>());
        await _caller.Received(1).SendCoreAsync(
            ChatHub.ReceiveError,
            Arg.Is<object?[]>(arguments =>
                (string)arguments![0]! == ChatHub.Errors.InvalidChatRoomId.Message),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMessage_Always_ForwardsTheConnectionAbortedToken()
    {
        // A participant who closed the tab must not keep the database and the broker busy on their behalf.
        await CreateHub().SendMessage(RoomId, "hello team");

        await _sender.Received(1).Send(Arg.Any<PostMessageCommand>(), _connectionAborted.Token);
    }

    // ---------------------------------------------------------------------------------------------
    // JoinRoom
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task JoinRoom_ValidRoom_AddsTheConnectionToTheRoomGroup()
    {
        await CreateHub().JoinRoom(RoomId);

        await _groups.Received(1).AddToGroupAsync(
            ConnectionId, ChatHub.GroupFor(ChatRoom), _connectionAborted.Token);
    }

    [Fact]
    public async Task JoinRoom_ValidRoom_ReturnsTheHistoryUntouched()
    {
        IReadOnlyList<MessageDto> history = [Message("first"), Message("second"), Message("third")];
        _sender.Send(Arg.Any<GetLatestMessagesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(history));

        IReadOnlyList<MessageDto> returned = await CreateHub().JoinRoom(RoomId);

        // Same instance: the query already returns oldest to newest, so the hub must not re-sort or re-page.
        returned.Should().BeSameAs(history);
    }

    [Fact]
    public async Task JoinRoom_Always_JoinsTheGroupBeforeReadingTheHistory()
    {
        // Reading first would drop any post committed between the read and the subscription. In this
        // order the worst case is a duplicate, which the page removes by message id.
        await CreateHub().JoinRoom(RoomId);

        Received.InOrder(() =>
        {
            _groups.AddToGroupAsync(ConnectionId, ChatHub.GroupFor(ChatRoom), Arg.Any<CancellationToken>());
            _sender.Send(Arg.Any<GetLatestMessagesQuery>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task JoinRoom_Always_RequestsTheChallengeLimitAndNoMore()
    {
        await CreateHub().JoinRoom(RoomId);

        await _sender.Received(1).Send(
            Arg.Is<GetLatestMessagesQuery>(query =>
                query!.ChatRoomId == ChatRoom && query!.Count == MessageConstants.LatestMessagesCount),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void JoinRoom_Signature_AcceptsOnlyTheRoom()
    {
        // No count on the wire, so no client can widen the read past the challenge's 50.
        ParameterInfo[] parameters = typeof(ChatHub).GetMethod(nameof(ChatHub.JoinRoom))!.GetParameters();

        parameters.Select(parameter => parameter.ParameterType).Should().Equal(typeof(Guid));
    }

    [Fact]
    public async Task JoinRoom_UnknownRoom_ReportsToTheCallerAndReturnsNoHistory()
    {
        _sender.Send(Arg.Any<GetLatestMessagesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<MessageDto>>(ChatRoomErrors.NotFound));

        IReadOnlyList<MessageDto> returned = await CreateHub().JoinRoom(RoomId);

        returned.Should().BeEmpty();
        await _caller.Received(1).SendCoreAsync(
            ChatHub.ReceiveError,
            Arg.Is<object?[]>(arguments => (string)arguments![0]! == ChatRoomErrors.NotFound.Message),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinRoom_EmptyRoomId_IsRejectedWithoutJoiningAGroupOrDispatchingAQuery()
    {
        IReadOnlyList<MessageDto> returned = await CreateHub().JoinRoom(Guid.Empty);

        returned.Should().BeEmpty();
        await _groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sender.DidNotReceive().Send(Arg.Any<GetLatestMessagesQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinRoom_Always_ForwardsTheConnectionAbortedToken()
    {
        await CreateHub().JoinRoom(RoomId);

        await _sender.Received(1).Send(Arg.Any<GetLatestMessagesQuery>(), _connectionAborted.Token);
    }

    // ---------------------------------------------------------------------------------------------
    // Switching rooms
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The correctness rule of the multiple-rooms bonus. Without this a participant who switches keeps
    /// receiving the previous room's posts, so two rooms would bleed into one window.
    /// </summary>
    [Fact]
    public async Task JoinRoom_AfterJoiningAnother_LeavesThePreviousRoomsGroup()
    {
        Guid otherRoomId = Guid.CreateVersion7();
        ChatHub hub = CreateHub();

        await hub.JoinRoom(RoomId);
        await hub.JoinRoom(otherRoomId);

        await _groups.Received(1).RemoveFromGroupAsync(
            ConnectionId, ChatHub.GroupFor(ChatRoom), _connectionAborted.Token);
        await _groups.Received(1).AddToGroupAsync(
            ConnectionId, ChatHub.GroupFor(new ChatRoomId(otherRoomId)), _connectionAborted.Token);
    }

    /// <summary>
    /// Leaving must come first. Adding first would leave a window in which the connection is in both
    /// groups, and a post from the old room would render into the new room's list.
    /// </summary>
    [Fact]
    public async Task JoinRoom_AfterJoiningAnother_LeavesBeforeItJoins()
    {
        Guid otherRoomId = Guid.CreateVersion7();
        ChatHub hub = CreateHub();

        await hub.JoinRoom(RoomId);
        await hub.JoinRoom(otherRoomId);

        Received.InOrder(() =>
        {
            _groups.RemoveFromGroupAsync(ConnectionId, ChatHub.GroupFor(ChatRoom), Arg.Any<CancellationToken>());
            _groups.AddToGroupAsync(
                ConnectionId, ChatHub.GroupFor(new ChatRoomId(otherRoomId)), Arg.Any<CancellationToken>());
        });
    }

    /// <summary>
    /// The reconnect path re-joins the room it is already in. Removing the group there would drop the
    /// membership the reconnect exists to restore.
    /// </summary>
    [Fact]
    public async Task JoinRoom_TheSameRoomTwice_DoesNotLeaveTheGroup()
    {
        ChatHub hub = CreateHub();

        await hub.JoinRoom(RoomId);
        await hub.JoinRoom(RoomId);

        await _groups.DidNotReceive().RemoveFromGroupAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinRoom_FirstRoomOfTheConnection_LeavesNothing()
    {
        await CreateHub().JoinRoom(RoomId);

        await _groups.DidNotReceive().RemoveFromGroupAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------------------------
    // CreateRoom
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateRoom_ValidName_ReturnsTheRoomAndReportsNothing()
    {
        ChatRoomDto created = new(Guid.CreateVersion7(), "Trading");
        _sender.Send(Arg.Any<CreateRoomCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(created));

        ChatRoomDto? returned = await CreateHub().CreateRoom("Trading");

        returned.Should().Be(created);
        await _caller.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Creating is not joining. A participant may create a room and stay where they are, and the client
    /// calls <c>JoinRoom</c> next — so this method must not move the connection between groups.
    /// </summary>
    [Fact]
    public async Task CreateRoom_ValidName_DoesNotJoinTheCallerToIt()
    {
        _sender.Send(Arg.Any<CreateRoomCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ChatRoomDto(Guid.CreateVersion7(), "Trading")));

        await CreateHub().CreateRoom("Trading");

        await _groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRoom_RefusedName_ReturnsNullAndTellsTheCallerAlone()
    {
        _sender.Send(Arg.Any<CreateRoomCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ChatRoomDto>(CreateRoomCommand.Errors.NameTaken));

        ChatRoomDto? returned = await CreateHub().CreateRoom("General");

        returned.Should().BeNull();
        await _caller.Received(1).SendCoreAsync(
            ChatHub.ReceiveError,
            Arg.Is<object?[]>(arguments =>
                (string)arguments![0]! == CreateRoomCommand.Errors.NameTaken.Message),
            Arg.Any<CancellationToken>());
        _clients.DidNotReceive().Group(Arg.Any<string>());
    }

    /// <summary>
    /// The name is passed through untouched: normalisation belongs to <c>RoomName</c>, and a hub that
    /// trimmed first would be a second place where the rules live.
    /// </summary>
    [Fact]
    public async Task CreateRoom_Always_PassesTheNameThroughUnchanged()
    {
        const string typed = "  Trading   Desk  ";
        _sender.Send(Arg.Any<CreateRoomCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ChatRoomDto(Guid.CreateVersion7(), "Trading Desk")));

        await CreateHub().CreateRoom(typed);

        await _sender.Received(1).Send(
            Arg.Is<CreateRoomCommand>(command => command!.Name == typed),
            _connectionAborted.Token);
    }

    [Fact]
    public void CreateRoom_Signature_AcceptsOnlyTheName()
    {
        // No creator on the wire: a room has no owner, and identity never comes from a payload anyway.
        ParameterInfo[] parameters = typeof(ChatHub).GetMethod(nameof(ChatHub.CreateRoom))!.GetParameters();

        parameters.Select(parameter => parameter.ParameterType).Should().Equal(typeof(string));
    }

    // ---------------------------------------------------------------------------------------------
    // Group naming and state
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GroupFor_TwoRooms_ProduceDistinctGroupNames()
    {
        ChatHub.GroupFor(ChatRoomId.New()).Should().NotBe(ChatHub.GroupFor(ChatRoomId.New()));
    }

    [Fact]
    public void Hub_KeepsNoMutableState_SoADisconnectHasNothingToCleanUp()
    {
        // OnDisconnectedAsync is deliberately not overridden: SignalR removes a closed connection from
        // every group it joined, and the one piece of per-connection state — the room currently joined —
        // lives in HubCallerContext.Items, which SignalR frees with the connection. A map this class owned
        // would have to be cleaned up, and that is what the resource budget rules out.
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        typeof(ChatHub).GetFields(declared)
            .Should().OnlyContain(
                field => field.IsLiteral || field.FieldType == typeof(ISender),
                "the only thing the hub holds is the dispatcher it was constructed with");

        typeof(ChatHub).GetMethod(nameof(Hub.OnDisconnectedAsync))!.DeclaringType
            .Should().Be<Hub>();
    }
}
