using System.Reflection;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Realtime;
using Chat.Application.Abstractions.Stocks;
using Chat.Application.Abstractions.Time;
using Chat.Domain.ChatRooms;
using Chat.Domain.Messages;

namespace Chat.UnitTests.Application;

/// <summary>
/// Turns the conventions task 1.6 exists to establish into rules the build enforces, rather than prose
/// in a plan that a later task can quietly break.
/// </summary>
public sealed class AbstractionsTests
{
    /// <summary>
    /// Every port a use case depends on. Adding one here is intentional: it opts the new abstraction
    /// into all the rules below.
    /// </summary>
    private static readonly Type[] PortTypes =
    [
        typeof(IMessageRepository),
        typeof(IChatRoomRepository),
        typeof(IUnitOfWork),
        typeof(IChatNotifier),
        typeof(IChatRoomNotifier),
        typeof(IStockQuoteRequester),
        typeof(IStockQuoteResponder),
        typeof(IStockQuoteProvider),
        typeof(IDateTimeProvider),
    ];

    public static TheoryData<Type> Ports()
    {
        TheoryData<Type> ports = [];

        foreach (Type port in PortTypes)
        {
            ports.Add(port);
        }

        return ports;
    }

    /// <summary>
    /// Staging methods (<c>Add</c>) are synchronous on purpose — they record an in-memory change and do
    /// no I/O, and a handler test asserts <c>DidNotReceive().Add(...)</c> to prove a stock command was
    /// never persisted. <see cref="IDateTimeProvider"/> reads a clock, which is likewise not I/O.
    /// </summary>
    private static readonly HashSet<string> SynchronousByDesign =
    [
        $"{nameof(IMessageRepository)}.{nameof(IMessageRepository.Add)}",
        $"{nameof(IChatRoomRepository)}.{nameof(IChatRoomRepository.Add)}",
    ];

    [Theory]
    [MemberData(nameof(Ports))]
    public void Port_AsynchronousMethods_ReturnATaskType(Type port)
    {
        foreach (MethodInfo method in AsynchronousMethodsOf(port))
        {
            bool returnsTask = method.ReturnType == typeof(Task)
                || (method.ReturnType.IsGenericType
                    && (method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
                        || method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)));

            returnsTask.Should().BeTrue(
                "{0}.{1} does I/O and must return Task, Task<T> or ValueTask<T> but returns {2}",
                port.Name,
                method.Name,
                method.ReturnType.Name);
        }
    }

    [Theory]
    [MemberData(nameof(Ports))]
    public void Port_AsynchronousMethods_TakeACancellationTokenLast(Type port)
    {
        foreach (MethodInfo method in AsynchronousMethodsOf(port))
        {
            ParameterInfo[] parameters = method.GetParameters();

            parameters.Should().NotBeEmpty(
                "{0}.{1} must accept a CancellationToken", port.Name, method.Name);

            parameters[^1].ParameterType.Should().Be<CancellationToken>(
                "{0}.{1} must take a CancellationToken as its last parameter", port.Name, method.Name);
        }
    }

    [Theory]
    [MemberData(nameof(Ports))]
    public void Port_NoMember_LeaksADomainEntityOrAnIQueryable(Type port)
    {
        Type[] forbidden = [typeof(Message), typeof(ChatRoom)];

        foreach (MethodInfo method in port.GetMethods())
        {
            // Write methods legitimately take an aggregate; it is the read side that must not return one.
            Type returned = UnwrapTask(method.ReturnType);

            forbidden.Should().NotContain(
                returned,
                "{0}.{1} returns the {2} entity; read paths must return a DTO",
                port.Name,
                method.Name,
                returned.Name);

            typeof(IQueryable).IsAssignableFrom(returned).Should().BeFalse(
                "{0}.{1} returns IQueryable, which would let a caller compose a query outside the repository",
                port.Name,
                method.Name);
        }
    }

    /// <summary>
    /// Guards the rules above against passing vacuously: if a refactor made the reflection find nothing,
    /// every other test here would still be green while enforcing nothing.
    /// </summary>
    [Fact]
    public void Ports_ExposeTheAsynchronousSurfaceTheRulesAreMeantToCover()
    {
        string[] discovered = PortTypes
            .SelectMany(port => AsynchronousMethodsOf(port).Select(method => $"{port.Name}.{method.Name}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        discovered.Should().BeEquivalentTo(
        [
            "IChatNotifier.BroadcastMessageAsync",
            "IChatNotifier.NotifyAlertAsync",
            "IChatRoomNotifier.RoomCreatedAsync",
            "IChatRoomRepository.ExistsAsync",
            "IChatRoomRepository.FindByNameAsync",
            "IChatRoomRepository.ListAsync",
            "IMessageRepository.GetLatestAsync",
            "IStockQuoteProvider.GetQuoteAsync",
            "IStockQuoteRequester.RequestAsync",
            "IStockQuoteResponder.RespondAsync",
            "IUnitOfWork.SaveChangesAsync",
        ]);
    }

    [Fact]
    public void Application_ReferencesNoInfrastructureFramework()
    {
        string[] forbidden =
        [
            "Microsoft.EntityFrameworkCore",
            "MassTransit",
            "Microsoft.AspNetCore",
            "RabbitMQ.Client",
        ];

        IEnumerable<string> referenced = typeof(IMessageRepository).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty);

        foreach (string name in referenced)
        {
            forbidden.Should().NotContain(
                candidate => name.StartsWith(candidate, StringComparison.Ordinal),
                "Chat.Application must depend only on Chat.Domain, but references {0}",
                name);
        }
    }

    private static IEnumerable<MethodInfo> AsynchronousMethodsOf(Type port) =>
        port.GetMethods().Where(method => !SynchronousByDesign.Contains($"{port.Name}.{method.Name}")
            && !method.IsSpecialName);

    private static Type UnwrapTask(Type type) =>
        type.IsGenericType
        && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>))
            ? type.GetGenericArguments()[0]
            : type;
}
