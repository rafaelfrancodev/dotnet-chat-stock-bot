using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Time;
using Chat.Infrastructure;
using Chat.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Covers the composition root's contract: a host either gets a fully wired persistence layer or a
/// startup failure that says what to fix. Nothing here opens a connection.
/// </summary>
public sealed class AddPersistenceTests
{
    private const string ConnectionString =
        "Server=127.0.0.1,1433;Database=ChatDb;User Id=sa;Password=not-used;Encrypt=False";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddPersistence_WithoutAConnectionString_FailsFastWithAnActionableMessage(string? connectionString)
    {
        ServiceCollection services = [];

        Action registration = () => services.AddPersistence(ConfigurationWith(connectionString));

        registration.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:ChatDatabase*user-secrets*README*");
    }

    [Theory]
    [InlineData(typeof(ChatDbContext))]
    [InlineData(typeof(IChatRoomRepository))]
    [InlineData(typeof(IMessageRepository))]
    [InlineData(typeof(IUnitOfWork))]
    public void AddPersistence_WithAConnectionString_ResolvesThePersistencePorts(Type port)
    {
        ServiceCollection services = [];
        services.AddPersistence(ConfigurationWith(ConnectionString));

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService(port).Should().NotBeNull();
    }

    [Fact]
    public void AddSystemClock_RegistersAClockThatReadsUtc()
    {
        ServiceCollection services = [];
        services.AddSystemClock();

        using ServiceProvider provider = services.BuildServiceProvider();
        IDateTimeProvider clock = provider.GetRequiredService<IDateTimeProvider>();

        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
        clock.UtcNow.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    private static IConfiguration ConfigurationWith(string? connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ChatDatabase"] = connectionString,
            })
            .Build();
}
