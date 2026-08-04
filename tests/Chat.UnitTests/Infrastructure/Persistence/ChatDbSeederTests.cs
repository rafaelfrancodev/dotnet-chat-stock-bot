using Chat.Application.Abstractions.Time;
using Chat.Domain.ChatRooms;
using Chat.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Chat.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Covers the startup seeding contract: after any number of boots, by any number of instances, the
/// database holds exactly one room named <c>General</c>, created through the domain factory.
/// <para>
/// These run against SQLite in process memory rather than a container: the mapping is real, the unique
/// index on <c>ChatRooms.Name</c> is genuinely enforced, and nothing here needs Docker. Migrations are
/// provider-specific and therefore not exercised here — <c>ApplyMigrationsAsync</c> is verified against
/// the real SQL Server instead (recorded in <c>docs/PLAN.md</c>, task 1.11).
/// </para>
/// </summary>
public sealed class ChatDbSeederTests : IDisposable
{
    private static readonly DateTimeOffset SeedInstant = new(2026, 8, 4, 22, 30, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = TestDatabase.OpenInMemoryDatabase();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task SeedAsync_EmptyDatabase_CreatesTheDefaultRoom()
    {
        await using ChatDbContext context = TestDatabase.CreateInMemoryContext(_connection);

        await SeederOver(context).SeedAsync(CancellationToken.None);

        ChatRoom room = await ReadTheOnlyRoomAsync();
        room.Name.Value.Should().Be("General");
    }

    [Fact]
    public async Task SeedAsync_EmptyDatabase_StampsTheRoomWithTheInjectedClock()
    {
        await using ChatDbContext context = TestDatabase.CreateInMemoryContext(_connection);

        await SeederOver(context).SeedAsync(CancellationToken.None);

        ChatRoom room = await ReadTheOnlyRoomAsync();
        room.CreatedAtUtc.Should().Be(SeedInstant, "the seeder must take its instant from IDateTimeProvider, not from the system clock");
    }

    [Fact]
    public async Task SeedAsync_CalledTwiceOnTheSameContext_LeavesExactlyOneRoom()
    {
        await using ChatDbContext context = TestDatabase.CreateInMemoryContext(_connection);
        ChatDbSeeder seeder = SeederOver(context);

        await seeder.SeedAsync(CancellationToken.None);
        await seeder.SeedAsync(CancellationToken.None);

        (await CountRoomsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SeedAsync_OnASecondBoot_LeavesTheExistingRoomUntouched()
    {
        Guid firstBootRoomId = await SeedOnAFreshHostAsync();

        await SeedOnAFreshHostAsync();

        ChatRoom room = await ReadTheOnlyRoomAsync();
        room.Id.Value.Should().Be(firstBootRoomId, "re-seeding must not replace the room every host restart");
    }

    [Fact]
    public async Task SeedAsync_LosingTheInsertRaceToAnotherInstance_SucceedsWithoutDuplicating()
    {
        // Two hosts starting together both see an empty ChatRooms table. The other one commits while
        // this one is saving, so only the unique index on Name can settle it.
        ConcurrentInstanceInterceptor other = new(_connection);
        await using ChatDbContext context = TestDatabase.CreateInMemoryContext(_connection, other);

        Func<Task> seeding = () => SeederOver(context).SeedAsync(CancellationToken.None);

        await seeding.Should().NotThrowAsync();
        other.Inserted.Should().BeTrue("the test is meaningless unless the race actually happened");
        (await CountRoomsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SeedAsync_SaveFailingForAnyOtherReason_Rethrows()
    {
        // The recovery above must not become a blanket "ignore write failures": a database that cannot
        // accept the room has to stop the host at startup instead of serving a chat with no room.
        await using ChatDbContext context =
            TestDatabase.CreateInMemoryContext(_connection, new UnavailableDatabaseInterceptor());

        Func<Task> seeding = () => SeederOver(context).SeedAsync(CancellationToken.None);

        await seeding.Should().ThrowAsync<DbUpdateException>();
        (await CountRoomsAsync()).Should().Be(0);
    }

    [Fact]
    public void DefaultRoomName_IsAValidRoomName()
    {
        RoomName.Create(ChatDbSeeder.DefaultRoomName).IsSuccess.Should().BeTrue();
    }

    private static ChatDbSeeder SeederOver(ChatDbContext context)
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(SeedInstant);

        return new ChatDbSeeder(context, clock, NullLogger<ChatDbSeeder>.Instance);
    }

    /// <summary>Seeds through a context of its own, the way a restarted host would.</summary>
    private async Task<Guid> SeedOnAFreshHostAsync()
    {
        await using ChatDbContext context = TestDatabase.CreateInMemoryContext(_connection);

        await SeederOver(context).SeedAsync(CancellationToken.None);

        return (await ReadTheOnlyRoomAsync()).Id.Value;
    }

    private async Task<ChatRoom> ReadTheOnlyRoomAsync()
    {
        await using ChatDbContext context = TestDatabase.CreateInMemoryContext(_connection);

        return await context.ChatRooms.AsNoTracking().SingleAsync(CancellationToken.None);
    }

    private async Task<int> CountRoomsAsync()
    {
        await using ChatDbContext context = TestDatabase.CreateInMemoryContext(_connection);

        return await context.ChatRooms.CountAsync(CancellationToken.None);
    }

    /// <summary>
    /// Stands in for a second host that commits the default room in the window between this seeder's
    /// existence check and its insert. Runs before the save's own transaction opens, so the duplicate
    /// is already committed when the insert reaches the unique index.
    /// </summary>
    private sealed class ConcurrentInstanceInterceptor(SqliteConnection connection) : SaveChangesInterceptor
    {
        public bool Inserted { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Inserted)
            {
                return result;
            }

            Inserted = true;

            await using ChatDbContext otherInstance = TestDatabase.CreateInMemoryContext(connection);

            RoomName name = RoomName.Create(ChatDbSeeder.DefaultRoomName).Value;
            otherInstance.ChatRooms.Add(ChatRoom.Create(name, SeedInstant).Value);

            await otherInstance.SaveChangesAsync(cancellationToken);

            return result;
        }
    }

    /// <summary>A write failure that is not a lost race, and therefore must not be swallowed.</summary>
    private sealed class UnavailableDatabaseInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("The database is unavailable.");
    }
}
