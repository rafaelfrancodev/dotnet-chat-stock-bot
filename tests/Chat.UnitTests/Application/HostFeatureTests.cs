using Chat.Application;
using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Realtime;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.UnitTests.Application;

/// <summary>
/// The two hosts share one Application assembly, so each handler must say which process runs it.
/// A handler that forgets is not a build error on its own — it simply never gets registered, and shows
/// up as a confusing "no handler for request" at runtime. These tests make it a build error instead.
/// </summary>
public sealed class HostFeatureTests
{
    private static readonly Type[] HandlerTypes = typeof(AssemblyReference).Assembly
        .GetTypes()
        .Where(type => type is { IsAbstract: false, IsInterface: false })
        .Where(type => type.GetInterfaces().Any(contract =>
            contract.IsGenericType
            && (contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                || contract.GetGenericTypeDefinition() == typeof(IRequestHandler<>))))
        .ToArray();

    [Fact]
    public void EveryHandler_DeclaresExactlyOneHostThatRunsIt()
    {
        HandlerTypes.Should().NotBeEmpty("the reflection must actually find handlers, or this test proves nothing");

        foreach (Type handler in HandlerTypes)
        {
            int markers =
                (typeof(IWebFeature).IsAssignableFrom(handler) ? 1 : 0)
                + (typeof(IBotFeature).IsAssignableFrom(handler) ? 1 : 0);

            markers.Should().Be(
                1,
                "{0} must implement exactly one of IWebFeature or IBotFeature so a host registers it",
                handler.Name);
        }
    }

    [Fact]
    public void BotHost_RegistersNoHandlerThatNeedsPersistenceOrTheChatSurface()
    {
        Type[] forbidden = [typeof(IChatRoomRepository), typeof(IMessageRepository), typeof(IUnitOfWork), typeof(IChatNotifier)];

        IEnumerable<Type> botHandlers = HandlerTypes.Where(type => typeof(IBotFeature).IsAssignableFrom(type));

        foreach (Type handler in botHandlers)
        {
            IEnumerable<Type> dependencies = handler
                .GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType);

            foreach (Type dependency in dependencies)
            {
                forbidden.Should().NotContain(
                    dependency,
                    "{0} runs in Chat.Bot, which never calls AddPersistence — depending on {1} would "
                    + "either fail at startup or force the bot to reach the database it must stay away from",
                    handler.Name,
                    dependency.Name);
            }
        }
    }

    [Fact]
    public void WebHost_RegistersOnlyItsOwnHandlers()
    {
        ServiceCollection services = [];
        services.AddApplication<IWebFeature>();

        IEnumerable<Type> registered = services
            .Select(descriptor => descriptor.ImplementationType)
            .OfType<Type>()
            .Where(HandlerTypes.Contains);

        registered.Should().OnlyContain(
            type => typeof(IWebFeature).IsAssignableFrom(type),
            "AddApplication<IWebFeature>() must not register the bot's handlers");
    }

    [Fact]
    public void BotHost_RegistersOnlyItsOwnHandlers()
    {
        ServiceCollection services = [];
        services.AddApplication<IBotFeature>();

        IEnumerable<Type> registered = services
            .Select(descriptor => descriptor.ImplementationType)
            .OfType<Type>()
            .Where(HandlerTypes.Contains);

        // Asserted as an absence rather than with OnlyContain: the bot legitimately has no handlers yet
        // (its own arrives in task 1.15), and an empty set is the correct outcome today.
        registered.Should().NotContain(
            type => typeof(IWebFeature).IsAssignableFrom(type),
            "AddApplication<IBotFeature>() must not register Chat.Web's handlers — that is what broke "
            + "the bot's startup before the feature markers existed");
    }
}
