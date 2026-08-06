using Chat.Application.Contracts.Messages;
using Chat.IntegrationTests.Infrastructure;

namespace Chat.IntegrationTests;

/// <summary>
/// The evaluation the challenge describes — "open two browsers with two users" — as a test: two
/// authenticated SignalR clients in one room, each seeing the other's lines exactly once.
/// </summary>
/// <param name="fixture">The running application and its throwaway database.</param>
[Collection(ChatServerCollection.Name)]
public sealed class TwoParticipantTests(ChatServerFixture fixture)
{
    [DockerFact]
    public async Task SendMessage_TwoParticipantsInTheSameRoom_EachSeesBothLinesInTheSameOrder()
    {
        Guid roomId = await fixture.CreateRoomAsync($"two browsers {Guid.NewGuid():N}");
        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        using ChatParticipant bob = await fixture.RegisterParticipantAsync("Bob Brown");

        await using TestHubClient aliceClient = await fixture.ConnectAsync(alice);
        await using TestHubClient bobClient = await fixture.ConnectAsync(bob);
        await aliceClient.JoinRoomAsync(roomId);
        await bobClient.JoinRoomAsync(roomId);

        await aliceClient.SendMessageAsync(roomId, "hello Bob");
        await bobClient.SendMessageAsync(roomId, "hello Alice");

        MessageDto aliceSawFirst = await aliceClient.NextMessageAsync();
        MessageDto aliceSawSecond = await aliceClient.NextMessageAsync();
        MessageDto bobSawFirst = await bobClient.NextMessageAsync();
        MessageDto bobSawSecond = await bobClient.NextMessageAsync();

        // The group broadcast includes the sender, so each connection sees both posts — that is what makes a
        // reviewer's two windows show the same conversation.
        aliceSawFirst.Content.Should().Be("hello Bob");
        aliceSawFirst.AuthorDisplayName.Should().Be(alice.DisplayName);
        aliceSawSecond.Content.Should().Be("hello Alice");
        aliceSawSecond.AuthorDisplayName.Should().Be(bob.DisplayName);

        bobSawFirst.Should().BeEquivalentTo(aliceSawFirst, "both windows render the same post");
        bobSawSecond.Should().BeEquivalentTo(aliceSawSecond);

        (await fixture.CountMessagesAsync(roomId)).Should().Be(2, "each line is stored once, not once per client");
    }
}
