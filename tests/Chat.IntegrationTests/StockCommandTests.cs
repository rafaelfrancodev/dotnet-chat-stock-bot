using Chat.Application.Contracts.Messages;
using Chat.Application.Contracts.Messaging;
using Chat.IntegrationTests.Infrastructure;

namespace Chat.IntegrationTests;

/// <summary>
/// The challenge's hard constraint, from the outside: a <c>/stock=</c> line leaves the browser, reaches the
/// broker, and leaves no trace in the message store.
/// </summary>
/// <param name="fixture">The running application, its throwaway database and the in-memory bus.</param>
[Collection(ChatServerCollection.Name)]
public sealed class StockCommandTests(ChatServerFixture fixture)
{
    private const string StockCommand = "/stock=aapl.us";
    private const string NormalisedStockCode = "aapl.us";

    [DockerFact]
    public async Task SendMessage_StockCommand_PublishesABrokerRequestForTheRoomAndTheTicker()
    {
        Guid roomId = await fixture.CreateRoomAsync($"stock publish {Guid.NewGuid():N}");
        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        string aliceUserId = await fixture.UserIdAsync(alice);
        await using TestHubClient client = await fixture.ConnectAsync(alice);
        await client.JoinRoomAsync(roomId);

        await client.SendMessageAsync(roomId, StockCommand);

        // Reaching the broker at all is the assertion: the bot is a separate process, so an in-process call
        // would pass every other test in this file and still break the challenge's decoupling requirement.
        // One request per command is proved by RequestStockQuoteHandlerTests with Received(1) — see
        // ChatServerFixture.PublishedAsync for why cardinality is not re-proved against a live bus list.
        StockQuoteRequested request = await fixture.PublishedAsync<StockQuoteRequested>(
            message => message.ChatRoomId == roomId);

        request.StockCode.Should().Be(NormalisedStockCode);
        request.RequestedByUserId.Should().Be(aliceUserId, "identity comes from the ticket, never from the payload");
        request.RequestedByDisplayName.Should().Be(alice.DisplayName);
        request.RequestId.Should().NotBeEmpty();
    }

    [DockerFact]
    public async Task SendMessage_StockCommand_CreatesNoMessageRow()
    {
        Guid roomId = await fixture.CreateRoomAsync($"stock storage {Guid.NewGuid():N}");
        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        await using TestHubClient client = await fixture.ConnectAsync(alice);
        await client.JoinRoomAsync(roomId);

        await client.SendMessageAsync(roomId, StockCommand);

        // Waiting on the published request is what makes this deterministic: the command has finished being
        // handled, so "no row" is an outcome and not a race.
        await fixture.PublishedAsync<StockQuoteRequested>(message => message.ChatRoomId == roomId);

        (await fixture.CountMessagesAsync(roomId)).Should()
            .Be(0, "a stock command is a command, never a post: the room must hold nothing");
        (await fixture.CountCommandRowsAsync()).Should()
            .Be(0, "SELECT COUNT(*) FROM Messages WHERE Content LIKE '/%' must be zero for the whole table");
    }

    [DockerFact]
    public async Task SendMessage_StockCommand_IsNeverBroadcastToTheRoom()
    {
        Guid roomId = await fixture.CreateRoomAsync($"stock silence {Guid.NewGuid():N}");
        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        await using TestHubClient client = await fixture.ConnectAsync(alice);
        await client.JoinRoomAsync(roomId);

        await client.SendMessageAsync(roomId, StockCommand);
        await client.SendMessageAsync(roomId, "an ordinary line");

        // The ordinary line is a barrier rather than a delay: SignalR delivers to one connection in order,
        // so if the command had been broadcast it would have arrived first. Waiting for a timeout to expire
        // would prove the same thing far more slowly and far less reliably.
        MessageDto firstPush = await client.NextMessageAsync();

        firstPush.Content.Should().Be("an ordinary line");
        client.Errors.Should().BeEmpty("a valid command is not an error the caller has to be told about");
    }
}
