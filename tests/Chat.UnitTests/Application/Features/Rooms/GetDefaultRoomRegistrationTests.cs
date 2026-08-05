using Chat.Application;
using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Contracts.Rooms;
using Chat.Application.Features.Rooms.GetDefaultRoom;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Chat.UnitTests.Application.Features.Rooms;

/// <summary>
/// The handler is <c>internal</c> and found by assembly scanning, so a wiring mistake would surface as a
/// runtime resolution failure while a page is rendering rather than as a build error. These tests make
/// it a build error, and pin that the query belongs to the web host alone.
/// </summary>
public sealed class GetDefaultRoomRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddApplication<IWebFeature>();
        services.AddSingleton(Substitute.For<IChatRoomRepository>());

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddApplication_RegistersTheInternalQueryHandler()
    {
        using ServiceProvider provider = BuildProvider();

        provider.GetService<IRequestHandler<GetDefaultRoomQuery, Result<ChatRoomDto>>>()
            .Should().NotBeNull();
    }

    [Fact]
    public async Task Send_SeededRoom_ReachesTheHandlerThroughThePipeline()
    {
        ChatRoomDto room = new(Guid.CreateVersion7(), ChatRoomConstants.DefaultRoomName);
        using ServiceProvider provider = BuildProvider();
        provider.GetRequiredService<IChatRoomRepository>()
            .FindByNameAsync(Arg.Any<RoomName>(), Arg.Any<CancellationToken>())
            .Returns(room);

        Result<ChatRoomDto> result = await provider
            .GetRequiredService<ISender>()
            .Send(new GetDefaultRoomQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(room);
    }
}
