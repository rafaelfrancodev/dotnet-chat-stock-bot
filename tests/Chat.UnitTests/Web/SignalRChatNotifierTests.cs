using Chat.Application.Contracts.Messages;
using Chat.Domain.ChatRooms;
using Chat.Domain.Messages;
using Chat.Web.Hubs;
using Chat.Web.Realtime;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Chat.UnitTests.Web;

/// <summary>
/// The outbound half of the hub. Its one rule is that a broadcast is scoped to the room's group: a post
/// must never reach a connection that did not join that room, which is both the privacy boundary once
/// several rooms exist and the resource control the challenge asks for.
/// </summary>
public sealed class SignalRChatNotifierTests : IDisposable
{
    private static readonly ChatRoomId RoomId = ChatRoomId.New();

    private readonly IHubContext<ChatHub> _hubContext = Substitute.For<IHubContext<ChatHub>>();
    private readonly IHubClients _clients = Substitute.For<IHubClients>();
    private readonly IClientProxy _group = Substitute.For<IClientProxy>();
    private readonly CancellationTokenSource _cancellation = new();

    public SignalRChatNotifierTests()
    {
        _hubContext.Clients.Returns(_clients);
        _clients.Group(Arg.Any<string>()).Returns(_group);
    }

    public void Dispose() => _cancellation.Dispose();

    private static MessageDto Message() =>
        new(Guid.CreateVersion7(), "Alice Anderson", "hello team", DateTimeOffset.UtcNow, MessageOrigin.Participant);

    [Fact]
    public async Task BroadcastMessageAsync_Always_SendsToTheRoomGroup()
    {
        MessageDto message = Message();

        await new SignalRChatNotifier(_hubContext).BroadcastMessageAsync(RoomId, message);

        _clients.Received(1).Group(ChatHub.GroupFor(RoomId));
        await _group.Received(1).SendCoreAsync(
            ChatHub.ReceiveMessage,
            Arg.Is<object?[]>(arguments => arguments!.Length == 1 && ReferenceEquals(arguments![0], message)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BroadcastMessageAsync_Always_NeverReachesEveryConnection()
    {
        await new SignalRChatNotifier(_hubContext).BroadcastMessageAsync(RoomId, Message());

        _ = _clients.DidNotReceive().All;
    }

    [Fact]
    public async Task BroadcastMessageAsync_Always_ForwardsTheCancellationToken()
    {
        await new SignalRChatNotifier(_hubContext)
            .BroadcastMessageAsync(RoomId, Message(), _cancellation.Token);

        await _group.Received(1).SendCoreAsync(
            ChatHub.ReceiveMessage, Arg.Any<object?[]>(), _cancellation.Token);
    }

    [Fact]
    public async Task BroadcastMessageAsync_NullMessage_Throws()
    {
        Func<Task> broadcast = () => new SignalRChatNotifier(_hubContext).BroadcastMessageAsync(RoomId, null!);

        await broadcast.Should().ThrowAsync<ArgumentNullException>();
    }
}
