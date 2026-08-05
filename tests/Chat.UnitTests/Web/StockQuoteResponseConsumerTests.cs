using Chat.Application.Contracts.Messaging;
using Chat.Application.Features.Messages.PostBotMessage;
using Chat.Domain.Common;
using Chat.Infrastructure.Messaging;
using Chat.Web.Messaging;
using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Chat.UnitTests.Web;

/// <summary>
/// Chat.Web's inbound adapter. What is worth pinning is the mapping from the wire contract onto the use
/// case — the bot's wording crossing unchanged, the outcome crossing so the banner decision can be made —
/// and that an answer the use case refuses neither faults the delivery nor is retried into a duplicate post.
/// </summary>
public sealed class StockQuoteResponseConsumerTests
{
    private const string RequesterId = "user-42";
    private const string Answer = "AAPL.US quote is $93.42 per share";

    private static readonly Guid RequestId = Guid.Parse("0198cd4d-2222-7000-8000-00000000abcd");
    private static readonly Guid RoomId = Guid.Parse("0198cd4d-3333-7000-8000-00000000abcd");

    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly List<PostBotMessageCommand> _dispatched = [];
    private readonly List<CancellationToken> _dispatchTokens = [];

    public StockQuoteResponseConsumerTests()
    {
        _sender
            .Send(Arg.Any<PostBotMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _dispatched.Add(call.Arg<PostBotMessageCommand>()!);
                _dispatchTokens.Add(call.Arg<CancellationToken>());

                return Result.Success();
            });
    }

    [Fact]
    public async Task Consume_StockQuoteResolved_DispatchesThePostBotMessageCommand()
    {
        await ConsumeAsync(Resolved());

        PostBotMessageCommand command = DispatchedCommand();
        command.ChatRoomId.Value.Should().Be(RoomId);
        command.Text.Should().Be(Answer);
        command.RequestedByUserId.Should().Be(RequesterId);
        command.Outcome.Should().Be(StockQuoteOutcome.Quoted);
    }

    /// <summary>
    /// The bot owns the wording, so this adapter must not prefix, trim or decorate it: the sentence the
    /// challenge grades exists in one place, in the bot.
    /// </summary>
    [Theory]
    [InlineData(StockQuoteOutcome.Quoted, "AAPL.US quote is $93.42 per share")]
    [InlineData(StockQuoteOutcome.SymbolNotFound, "Sorry, I could not find a quote for AAPL.XX.")]
    [InlineData(
        StockQuoteOutcome.LookupFailed,
        "I could not reach the quote service, so I have no price for AAPL.US right now.")]
    public async Task Consume_Always_PassesTheBotsTextAndOutcomeThrough(StockQuoteOutcome outcome, string text)
    {
        await ConsumeAsync(Resolved(outcome: outcome, message: text));

        PostBotMessageCommand command = DispatchedCommand();
        command.Text.Should().Be(text);
        command.Outcome.Should().Be(outcome, "the handler decides about the outage banner from it");
    }

    [Fact]
    public async Task Consume_Always_ForwardsTheConsumeCancellationToken()
    {
        using CancellationTokenSource cancellation = new();

        await ConsumeAsync(Resolved(), cancellation.Token);

        _dispatchTokens.Should().ContainSingle().Which.Should().Be(cancellation.Token);
    }

    /// <summary>
    /// A failed <see cref="Result"/> is deterministic: an identical redelivery reproduces it four times and
    /// then dead-letters something already understood. In the one case that matters — the answer saved but
    /// not broadcast — a redelivery would also duplicate the post, so acknowledging is the correct choice.
    /// </summary>
    [Fact]
    public async Task Consume_PostFails_AcknowledgesInsteadOfFaultingTheDelivery()
    {
        _sender
            .Send(Arg.Any<PostBotMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(PostBotMessageCommand.Errors.NotAnnounced));

        Func<Task> act = () => ConsumeAsync(Resolved());

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// An unexpected exception is left to propagate, which is what puts MassTransit's retry policy and then
    /// <c>stock-quote-responses_error</c> in charge. There is deliberately no manual nack path.
    /// </summary>
    [Fact]
    public async Task Consume_UseCaseThrows_LetsTheDeliveryFaultSoTheTransportRetriesAndDeadLetters()
    {
        _sender
            .Send(Arg.Any<PostBotMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns<Result>(_ => throw new InvalidOperationException("the database went away"));

        Func<Task> act = () => ConsumeAsync(Resolved());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Consume_NullContext_Throws()
    {
        Func<Task> act = () => CreateConsumer().Consume(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// End to end over MassTransit's in-memory harness — no broker: the consumer really is bound to
    /// <see cref="MessagingConstants.StockQuoteResponseQueue"/> (the queue whose absence made every answer
    /// unroutable until this task), really receives a published <see cref="StockQuoteResolved"/>, and
    /// dispatches the use case without faulting the delivery.
    /// </summary>
    [Fact]
    public async Task Consume_PublishedAnswer_ReachesTheConsumerOnTheResponseQueue()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_sender)
            .AddMassTransitTestHarness(configurator =>
                configurator.AddStockQuoteResponseConsumer<StockQuoteResponseConsumer>())
            .BuildServiceProvider(validateScopes: true);

        ITestHarness harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(Resolved());

        (await harness.Consumed.Any<StockQuoteResolved>()).Should().BeTrue();

        IReceivedMessage<StockQuoteResolved> received =
            harness.Consumed.Select<StockQuoteResolved>().Single();
        received.Exception.Should().BeNull("a well-formed answer must not fault the delivery");
        received.Context.ReceiveContext.InputAddress.AbsolutePath.Split('/')[^1]
            .Should().Be(MessagingConstants.StockQuoteResponseQueue);

        DispatchedCommand().Text.Should().Be(Answer);
    }

    private StockQuoteResponseConsumer CreateConsumer() =>
        new(_sender, NullLogger<StockQuoteResponseConsumer>.Instance);

    private Task ConsumeAsync(StockQuoteResolved response, CancellationToken cancellationToken = default)
    {
        ConsumeContext<StockQuoteResolved> context = Substitute.For<ConsumeContext<StockQuoteResolved>>();
        context.Message.Returns(response);
        context.CancellationToken.Returns(cancellationToken);

        return CreateConsumer().Consume(context);
    }

    private PostBotMessageCommand DispatchedCommand()
    {
        _dispatched.Should().HaveCount(1, "one delivery must produce exactly one use-case dispatch");

        return _dispatched[0];
    }

    private static StockQuoteResolved Resolved(
        StockQuoteOutcome outcome = StockQuoteOutcome.Quoted,
        string message = Answer) => new(
        RequestId: RequestId,
        ChatRoomId: RoomId,
        StockCode: "aapl.us",
        RequestedByUserId: RequesterId,
        Outcome: outcome,
        Price: outcome == StockQuoteOutcome.Quoted ? 93.42m : null,
        Message: message,
        ResolvedAtUtc: new DateTimeOffset(2026, 8, 5, 19, 15, 0, TimeSpan.Zero));
}
