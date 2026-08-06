using Chat.Application.Contracts.Messages;
using Chat.Application.Contracts.Messaging;
using Chat.IntegrationTests.Infrastructure;
using MassTransit.Testing;

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

        bool published = await fixture.Broker.Published.Any<StockQuoteRequested>(
            context => context.Context.Message.ChatRoomId == roomId);

        published.Should().BeTrue("the bot is reached through the broker, not by an in-process call");

        StockQuoteRequested request = fixture.Broker.Published
            .Select<StockQuoteRequested>(context => context.Context.Message.ChatRoomId == roomId)
            .Select(context => context.Context.Message)
            .Should()
            .ContainSingle("one command produces exactly one request")
            .Subject;

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
        await fixture.Broker.Published.Any<StockQuoteRequested>(
            context => context.Context.Message.ChatRoomId == roomId);

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
