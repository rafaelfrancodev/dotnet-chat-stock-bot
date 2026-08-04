using Chat.Application;
using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Contracts.Messages;
using Chat.Application.Features.Messages.GetLatestMessages;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Chat.UnitTests.Application.Features.Messages;

/// <summary>
/// The handler and the validator are <c>internal</c>: only the query is public surface. Assembly
/// scanning has to find them anyway, so these tests pin that the wiring still works — a mistake here
/// would surface as a runtime resolution failure in the hub, not as a build error.
/// </summary>
public sealed class GetLatestMessagesRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddApplication<IWebFeature>();
        services.AddSingleton(Substitute.For<IChatRoomRepository>());
        services.AddSingleton(Substitute.For<IMessageRepository>());

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddApplication_RegistersTheInternalQueryHandler()
    {
        using ServiceProvider provider = BuildProvider();

        provider.GetService<IRequestHandler<GetLatestMessagesQuery, Result<IReadOnlyList<MessageDto>>>>()
            .Should().NotBeNull();
    }

    [Fact]
    public void AddApplication_RegistersTheInternalValidator()
    {
        using ServiceProvider provider = BuildProvider();

        provider.GetServices<IValidator<GetLatestMessagesQuery>>()
            .Should().ContainSingle().Which.Should().BeOfType<GetLatestMessagesValidator>();
    }

    [Fact]
    public async Task Send_InvalidCount_IsRejectedByThePipelineAsAFailedResult()
    {
        using ServiceProvider provider = BuildProvider();
        ISender sender = provider.GetRequiredService<ISender>();

        Result<IReadOnlyList<MessageDto>> result = await sender.Send(
            new GetLatestMessagesQuery(ChatRoomId.New(), int.MaxValue),
            CancellationToken.None);

        // Validation failures are expected failures: a failed Result, never an exception.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation.Failed");
    }
}
