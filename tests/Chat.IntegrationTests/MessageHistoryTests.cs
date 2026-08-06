using Chat.Application.Contracts.Messages;
using Chat.Domain.Messages;
using Chat.IntegrationTests.Infrastructure;

namespace Chat.IntegrationTests;

/// <summary>
/// "Messages ordered by timestamp, only the last 50" — asserted through the hub, against SQL Server, with
/// the real query the chat window uses.
/// </summary>
/// <param name="fixture">The running application and its throwaway database.</param>
[Collection(ChatServerCollection.Name)]
public sealed class MessageHistoryTests(ChatServerFixture fixture)
{
    [DockerFact]
    public async Task SendMessage_SeveralLines_AreReadBackFromTheHistoryOldestFirst()
    {
        Guid roomId = await fixture.CreateRoomAsync($"history {Guid.NewGuid():N}");
        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        await using TestHubClient writer = await fixture.ConnectAsync(alice);
        await writer.JoinRoomAsync(roomId);

        string[] lines = ["first line", "second line", "third line"];

        foreach (string line in lines)
        {
            // Awaited one at a time: SendMessage returns only once the post is committed and broadcast,
            // so the order the posts reach the database is the order they were typed.
            await writer.SendMessageAsync(roomId, line);
        }

        // A second connection reads the history the way a reviewer's second browser does.
        await using TestHubClient reader = await fixture.ConnectAsync(alice);
        IReadOnlyList<MessageDto> history = await reader.JoinRoomAsync(roomId);

        history.Select(message => message.Content).Should().Equal(lines);
        history.Should().OnlyContain(message => message.AuthorDisplayName == alice.DisplayName);
        history.Should().OnlyContain(message => message.Origin == MessageOrigin.Participant);
        history.Select(message => message.PostedAtUtc).Should().BeInAscendingOrder();
        history.Should().OnlyContain(message => message.PostedAtUtc.Offset == TimeSpan.Zero);
    }

    [DockerFact]
    public async Task JoinRoom_MoreHistoryThanTheLimit_ReturnsOnlyTheNewestFiftyOldestFirst()
    {
        const int seeded = MessageConstants.LatestMessagesCount + 10;

        Guid roomId = await fixture.CreateRoomAsync($"capped {Guid.NewGuid():N}");
        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");

        // Written straight to the database with timestamps a second apart: the cap and the ordering are
        // properties of the read path, and sixty hub round trips would only make the test slower and its
        // ordering depend on clock resolution.
        IReadOnlyList<string> everything = await fixture.SeedParticipantHistoryAsync(
            roomId,
            seeded,
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));

        await using TestHubClient reader = await fixture.ConnectAsync(alice);
        IReadOnlyList<MessageDto> history = await reader.JoinRoomAsync(roomId);

        history.Should().HaveCount(MessageConstants.LatestMessagesCount);
        history.Select(message => message.Content).Should().Equal(
            everything.Skip(seeded - MessageConstants.LatestMessagesCount),
            "the window shows the newest fifty, still oldest first");
        history.Select(message => message.PostedAtUtc).Should().BeInAscendingOrder();
        (await fixture.CountMessagesAsync(roomId)).Should().Be(seeded, "nothing was dropped from the store");
    }
}
