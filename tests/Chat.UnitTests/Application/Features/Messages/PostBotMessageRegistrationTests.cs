using Chat.Application;
using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Realtime;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Messages;
using Chat.Application.Contracts.Messaging;
using Chat.Application.Contracts.Realtime;
using Chat.Application.Features.Messages.PostBotMessage;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.Messages;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Chat.UnitTests.Application.Features.Messages;

/// <summary>
/// The handler is <c>internal</c> and MediatR finds it by assembly scan, so a missed registration would
/// surface as "no handler for request" at the far end of the broker round trip rather than at build time.
/// These tests run the real composition — no broker, no database — to close that gap.
/// </summary>
public sealed class PostBotMessageRegistrationTests
{
    private static readonly ChatRoomId RoomId = ChatRoomId.New();

    private readonly IChatRoomRepository _chatRooms = Substitute.For<IChatRoomRepository>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IChatNotifier _notifier = Substitute.For<IChatNotifier>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public PostBotMessageRegistrationTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 5, 19, 15, 0, TimeSpan.Zero));
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
        services.AddSingleton(_clock);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddApplication_RegistersTheInternalCommandHandler()
    {
        using ServiceProvider provider = BuildProvider();

        provider.GetService<IRequestHandler<PostBotMessageCommand, Result>>().Should().NotBeNull();
    }

    [Fact]
    public async Task Send_ResolvedAnswer_PostsAsTheBotAndBroadcastsThroughThePipeline()
    {
        using ServiceProvider provider = BuildProvider();
        ISender sender = provider.GetRequiredService<ISender>();

        Result result = await sender.Send(
            new PostBotMessageCommand(RoomId, "AAPL.US quote is $93.42 per share", "user-1", StockQuoteOutcome.Quoted),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _messages.Received(1).Add(Arg.Is<Message>(message => message!.Origin == MessageOrigin.Bot));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.Received(1).BroadcastMessageAsync(RoomId, Arg.Any<MessageDto>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().NotifyAlertAsync(default!, default!, default);
    }

    [Fact]
    public async Task Send_FailedLookup_AlsoRaisesTheOutageAlertThroughThePipeline()
    {
        using ServiceProvider provider = BuildProvider();
        ISender sender = provider.GetRequiredService<ISender>();

        Result result = await sender.Send(
            new PostBotMessageCommand(
                RoomId,
                "I could not reach the quote service, so I have no price for AAPL.US right now.",
                "user-1",
                StockQuoteOutcome.LookupFailed),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _notifier.Received(1).NotifyAlertAsync(
            "user-1",
            ChatAlert.QuoteServiceUnavailable,
            Arg.Any<CancellationToken>());
    }
}
