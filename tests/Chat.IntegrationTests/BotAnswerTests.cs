using Chat.Application.Contracts.Messages;
using Chat.Application.Contracts.Messaging;
using Chat.Application.Features.StockCommands.ResolveStockQuote;
using Chat.Domain.Messages;
using Chat.Domain.StockCommands;
using Chat.IntegrationTests.Infrastructure;

namespace Chat.IntegrationTests;

/// <summary>
/// The other half of the broker round trip: an answer published on
/// <c>MessagingConstants.StockQuoteResponseQueue</c> — which is what <c>Chat.Bot</c> does — is posted into
/// the room as the bot and pushed to the connected participants.
/// </summary>
/// <remarks>
/// The answer is published onto the bus rather than produced by running <c>Chat.Bot</c> in-process, because
/// the two hosts are deliberately separate processes: the contract between them is
/// <see cref="StockQuoteResolved"/>, and that is what this test puts on the wire. The bot's own side — Stooq,
/// the CSV and the wording — is covered offline in <c>Chat.UnitTests</c>, and the whole flow is run for real
/// against RabbitMQ in task 1.18.
/// </remarks>
/// <param name="fixture">The running application, its throwaway database and the in-memory bus.</param>
[Collection(ChatServerCollection.Name)]
public sealed class BotAnswerTests(ChatServerFixture fixture)
{
    private const decimal Price = 93.42m;

    [DockerFact]
    public async Task StockQuoteResolved_ArrivingFromTheBot_IsPostedByTheBotAndReachesTheRoom()
    {
        Guid roomId = await fixture.CreateRoomAsync($"bot answer {Guid.NewGuid():N}");
        using ChatParticipant alice = await fixture.RegisterParticipantAsync("Alice Anderson");
        string aliceUserId = await fixture.UserIdAsync(alice);
        await using TestHubClient client = await fixture.ConnectAsync(alice);
        await client.JoinRoomAsync(roomId);

        StockCode code = StockCode.Create("aapl.us").Value;
        string answer = StockQuoteAnswer.Quoted(code, Price);

        await fixture.Broker.Bus.Publish(new StockQuoteResolved(
            RequestId: Guid.CreateVersion7(),
            ChatRoomId: roomId,
            StockCode: code.ToString(),
            RequestedByUserId: aliceUserId,
            Outcome: StockQuoteOutcome.Quoted,
            Price: Price,
            Message: answer,
            ResolvedAtUtc: DateTimeOffset.UtcNow));

        MessageDto posted = await client.NextMessageAsync();

        posted.Content.Should().Be(
            "AAPL.US quote is $93.42 per share",
            "this sentence is quoted verbatim by the challenge and must reach the room unchanged");
        posted.AuthorDisplayName.Should().Be(MessageAuthor.BotDisplayName, "the post owner is the bot");
        posted.Origin.Should().Be(MessageOrigin.Bot);

        (await fixture.CountMessagesAsync(roomId)).Should()
            .Be(1, "the bot's answer is persisted, unlike the command that asked for it");
    }
}
