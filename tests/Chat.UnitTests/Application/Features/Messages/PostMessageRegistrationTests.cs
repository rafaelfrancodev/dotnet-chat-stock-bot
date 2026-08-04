using Chat.Application;
using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Realtime;
using Chat.Application.Abstractions.Stocks;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Messaging;
using Chat.Application.Features.Messages.PostMessage;
using Chat.Application.Features.StockCommands.RequestStockQuote;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.Messages;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Chat.UnitTests.Application.Features.Messages;

/// <summary>
/// Both handlers and the validator are <c>internal</c>, and the stock branch dispatches a second request
/// through the mediator. These tests run the real composition so that the nested dispatch — and the "no
/// database write" guarantee across it — is exercised end to end without a broker or a database.
/// </summary>
public sealed class PostMessageRegistrationTests
{
    private static readonly ChatRoomId RoomId = ChatRoomId.New();

    private readonly IChatRoomRepository _chatRooms = Substitute.For<IChatRoomRepository>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IChatNotifier _notifier = Substitute.For<IChatNotifier>();
    private readonly IStockQuoteRequester _stockQuotes = Substitute.For<IStockQuoteRequester>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public PostMessageRegistrationTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 4, 12, 30, 0, TimeSpan.Zero));
        _chatRooms.ExistsAsync(RoomId, Arg.Any<CancellationToken>()).Returns(true);
    }

    private ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddApplication<IWebFeature>();
        services.AddSingleton(_chatRooms);
        services.AddSingleton(_messages);
        services.AddSingleton(_unitOfWork);
        services.AddSingleton(_notifier);
        services.AddSingleton(_stockQuotes);
        services.AddSingleton(_clock);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddApplication_RegistersTheInternalCommandHandlers()
    {
        using ServiceProvider provider = BuildProvider();

        provider.GetService<IRequestHandler<PostMessageCommand, Result<PostMessageOutcome>>>().Should().NotBeNull();
        provider.GetService<IRequestHandler<RequestStockQuoteCommand, Result>>().Should().NotBeNull();
    }

    [Fact]
    public void AddApplication_RegistersTheInternalValidator()
    {
        using ServiceProvider provider = BuildProvider();

        provider.GetServices<IValidator<PostMessageCommand>>()
            .Should().ContainSingle().Which.Should().BeOfType<PostMessageValidator>();
    }

    [Fact]
    public async Task Send_StockCommand_ReachesTheBrokerAndWritesNothing()
    {
        using ServiceProvider provider = BuildProvider();
        ISender sender = provider.GetRequiredService<ISender>();

        Result<PostMessageOutcome> result = await sender.Send(
            new PostMessageCommand(RoomId, "/stock=aapl.us", "user-1", "Alice"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(PostMessageOutcome.QuoteRequested);
        await _stockQuotes.Received(1).RequestAsync(
            Arg.Is<StockQuoteRequested>(request => request!.StockCode == "aapl.us"),
            Arg.Any<CancellationToken>());
        _messages.DidNotReceive().Add(Arg.Any<Message>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_PlainMessage_PersistsAndNeverTouchesTheBroker()
    {
        using ServiceProvider provider = BuildProvider();
        ISender sender = provider.GetRequiredService<ISender>();

        Result<PostMessageOutcome> result = await sender.Send(
            new PostMessageCommand(RoomId, "hello team", "user-1", "Alice"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(PostMessageOutcome.Posted);
        _messages.Received(1).Add(Arg.Any<Message>());
        await _stockQuotes.DidNotReceiveWithAnyArgs().RequestAsync(default!, default);
    }

    [Fact]
    public async Task Send_EmptyInput_IsRejectedByThePipelineAsAFailedResult()
    {
        using ServiceProvider provider = BuildProvider();
        ISender sender = provider.GetRequiredService<ISender>();

        Result<PostMessageOutcome> result = await sender.Send(
            new PostMessageCommand(RoomId, "   ", "user-1", "Alice"),
            CancellationToken.None);

        // Validation failures are expected failures: a failed Result, never an exception.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation.Failed");
        _messages.DidNotReceive().Add(Arg.Any<Message>());
    }
}
