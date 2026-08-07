using System.Net;
using Chat.Application.Contracts.Messages;
using Chat.Application.Contracts.Rooms;
using Chat.Application.Features.Rooms.CreateRoom;
using Chat.Domain.ChatRooms;
using Chat.IntegrationTests.Infrastructure;

namespace Chat.IntegrationTests;

/// <summary>
/// The multiple-chatrooms bonus, end to end over real SignalR connections: rooms can be created from the
/// chat window, they appear in other windows without a reload, and — the part that actually matters — a
/// room's traffic never reaches a participant who is in a different one.
/// </summary>
/// <remarks>
/// Isolation is asserted by waiting for silence rather than by inspecting group membership. A test that
/// checked the SignalR group would pass while the broadcast leaked through some other path; only "Bob's
/// window received nothing" states the guarantee a reviewer with two browsers would check.
/// </remarks>
/// <param name="fixture">The running application and its throwaway database.</param>
[Collection(ChatServerCollection.Name)]
public sealed class MultipleRoomTests(ChatServerFixture fixture)
{
    /// <summary>
    /// The bonus's real requirement. Without the hub leaving the previous group on a switch, or with a
    /// broadcast that ignored the room, Bob would see Alice's line and the rooms would be one room.
    /// </summary>
    [DockerFact]
    public async Task SendMessage_TwoRooms_ReachesOnlyTheRoomItWasPostedIn()
    {
        Guid alphaId = await fixture.CreateRoomAsync($"alpha {Guid.NewGuid():N}");
        Guid betaId = await fixture.CreateRoomAsync($"beta {Guid.NewGuid():N}");

        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        using ChatParticipant bob = await fixture.RegisterParticipantAsync("Bob Brown");

        await using TestHubClient aliceClient = await fixture.ConnectAsync(alice);
        await using TestHubClient bobClient = await fixture.ConnectAsync(bob);
        await aliceClient.JoinRoomAsync(alphaId);
        await bobClient.JoinRoomAsync(betaId);

        await aliceClient.SendMessageAsync(alphaId, "alpha only");

        MessageDto aliceSaw = await aliceClient.NextMessageAsync();
        aliceSaw.Content.Should().Be("alpha only");

        (await bobClient.TryNextMessageAsync()).Should().BeNull(
            "a post must never reach a participant who is in another room");

        (await fixture.CountMessagesAsync(alphaId)).Should().Be(1);
        (await fixture.CountMessagesAsync(betaId)).Should().Be(0, "the other room's history stays its own");
    }

    /// <summary>
    /// Switching rooms has to unsubscribe from the old one. This is the case a client-driven "leave" would
    /// get wrong the moment the client forgot to send it.
    /// </summary>
    [DockerFact]
    public async Task JoinRoom_AfterSwitching_StopsDeliveringThePreviousRoomsPosts()
    {
        Guid firstId = await fixture.CreateRoomAsync($"first {Guid.NewGuid():N}");
        Guid secondId = await fixture.CreateRoomAsync($"second {Guid.NewGuid():N}");

        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        using ChatParticipant bob = await fixture.RegisterParticipantAsync("Bob Brown");

        await using TestHubClient aliceClient = await fixture.ConnectAsync(alice);
        await using TestHubClient bobClient = await fixture.ConnectAsync(bob);

        // Bob starts in the first room with Alice, then moves to the second.
        await aliceClient.JoinRoomAsync(firstId);
        await bobClient.JoinRoomAsync(firstId);
        await bobClient.JoinRoomAsync(secondId);

        await aliceClient.SendMessageAsync(firstId, "still in the first room");

        (await aliceClient.NextMessageAsync()).Content.Should().Be("still in the first room");
        (await bobClient.TryNextMessageAsync()).Should().BeNull(
            "Bob left that room, so its posts must stop arriving");
    }

    /// <summary>
    /// Each room keeps its own history, and joining one loads only that room's last 50.
    /// </summary>
    [DockerFact]
    public async Task JoinRoom_EachRoom_LoadsOnlyItsOwnHistory()
    {
        Guid alphaId = await fixture.CreateRoomAsync($"history alpha {Guid.NewGuid():N}");
        Guid betaId = await fixture.CreateRoomAsync($"history beta {Guid.NewGuid():N}");

        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        await using TestHubClient client = await fixture.ConnectAsync(alice);

        await client.JoinRoomAsync(alphaId);
        await client.SendMessageAsync(alphaId, "in alpha");
        await client.NextMessageAsync();

        await client.JoinRoomAsync(betaId);
        await client.SendMessageAsync(betaId, "in beta");
        await client.NextMessageAsync();

        IReadOnlyList<MessageDto> alpha = await client.JoinRoomAsync(alphaId);
        IReadOnlyList<MessageDto> beta = await client.JoinRoomAsync(betaId);

        alpha.Select(message => message.Content).Should().Equal("in alpha");
        beta.Select(message => message.Content).Should().Equal("in beta");
    }

    /// <summary>
    /// Creating a room from the chat window: it comes back with an identifier the caller can join, and the
    /// row really exists.
    /// </summary>
    [DockerFact]
    public async Task CreateRoom_NewName_ReturnsAJoinableRoom()
    {
        string name = $"trading {Guid.NewGuid():N}";
        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        await using TestHubClient client = await fixture.ConnectAsync(alice);

        ChatRoomDto? created = await client.CreateRoomAsync(name);

        created.Should().NotBeNull();
        created!.Name.Should().Be(name);

        IReadOnlyList<MessageDto> history = await client.JoinRoomAsync(created.Id);
        history.Should().BeEmpty("a new room starts empty");

        await client.SendMessageAsync(created.Id, "first line here");
        (await client.NextMessageAsync()).Content.Should().Be("first line here");
    }

    /// <summary>
    /// The directory is pushed to everyone, so a second window can offer the new room without a reload.
    /// This is the one broadcast in the application that is not room-scoped, and it carries a name and an
    /// identifier only.
    /// </summary>
    [DockerFact]
    public async Task CreateRoom_NewName_IsAnnouncedToOtherConnections()
    {
        string name = $"announced {Guid.NewGuid():N}";
        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        using ChatParticipant bob = await fixture.RegisterParticipantAsync("Bob Brown");

        await using TestHubClient aliceClient = await fixture.ConnectAsync(alice);
        await using TestHubClient bobClient = await fixture.ConnectAsync(bob);

        ChatRoomDto? created = await aliceClient.CreateRoomAsync(name);

        ChatRoomDto announced = await bobClient.NextRoomAsync();
        announced.Id.Should().Be(created!.Id);
        announced.Name.Should().Be(name);
    }

    /// <summary>
    /// A duplicate name is refused with a sentence the participant can act on, not an exception — and the
    /// comparison is on the normalised name, so extra spaces do not sneak a second "General" past it.
    /// </summary>
    [DockerFact]
    public async Task CreateRoom_NameAlreadyTaken_IsRefusedWithAnError()
    {
        string name = $"duplicate {Guid.NewGuid():N}";
        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        await using TestHubClient client = await fixture.ConnectAsync(alice);

        (await client.CreateRoomAsync(name)).Should().NotBeNull();

        ChatRoomDto? second = await client.CreateRoomAsync($"  {name}   ");

        second.Should().BeNull();
        client.Errors.Should().ContainSingle()
            .Which.Should().Be(CreateRoomCommand.Errors.NameTaken.Message);
    }

    /// <summary>
    /// The symptom this feature exists to fix: the chat page offered one room and no way to reach another.
    /// Asserted on the rendered HTML, because a picker that works over the hub but is not on the page is
    /// exactly what "in the UI we have only 1 chatroom" means.
    /// </summary>
    [DockerFact]
    public async Task ChatPage_SeveralRooms_RendersAPickerContainingThemAll()
    {
        string extra = $"rendered {Guid.NewGuid():N}";
        await fixture.CreateRoomAsync(extra);

        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        using HttpResponseMessage page = await alice.Http.GetAsync("/Chat");
        string html = await page.Content.ReadAsStringAsync();

        page.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("id=\"chat-room\"", "the room picker must be on the page, not only in the hub");
        html.Should().Contain(extra, "a room created outside this window is still offered");
        html.Should().Contain(ChatRoomConstants.DefaultRoomName);
        html.Should().Contain("id=\"chat-new-room\"", "and a way to create one");
    }

    /// <summary>
    /// The landing room stays the seeded one however the list is ordered. Rooms sort by name, so a room
    /// named before "General" must not become everybody's landing room.
    /// </summary>
    [DockerFact]
    public async Task ChatPage_ARoomSortingBeforeTheSeededOne_StillOpensOnTheSeededRoom()
    {
        // "A…" sorts before "General", so this is the room a "first in the list" bug would open on.
        await fixture.CreateRoomAsync($"AAA {Guid.NewGuid():N}");

        IReadOnlyList<ChatRoomDto> rooms = await fixture.ListRoomsAsync();
        Guid seededId = rooms.Single(room => room.Name == ChatRoomConstants.DefaultRoomName).Id;

        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        using HttpResponseMessage page = await alice.Http.GetAsync("/Chat");
        string html = await page.Content.ReadAsStringAsync();

        html.Should().Contain($"data-room-id=\"{seededId}\"", "the window opens on the seeded room by name");
    }

    /// <summary>
    /// The seeded room is still there alongside anything created, so the challenge's single-room scenario
    /// keeps working and the page always has a room to open on.
    /// </summary>
    [DockerFact]
    public async Task CreateRoom_Always_LeavesTheSeededRoomInPlace()
    {
        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        await using TestHubClient client = await fixture.ConnectAsync(alice);

        await client.CreateRoomAsync($"extra {Guid.NewGuid():N}");

        IReadOnlyList<ChatRoomDto> rooms = await fixture.ListRoomsAsync();

        rooms.Should().Contain(room => room.Name == ChatRoomConstants.DefaultRoomName);
        rooms.Should().HaveCountGreaterThan(1, "the bonus adds rooms next to the seeded one");
    }
}
