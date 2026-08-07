using Chat.Application.Contracts.Rooms;
using Chat.Domain.ChatRooms;
using Chat.Infrastructure.Persistence;
using Chat.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Chat.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Covers the lookup the chat page opens on. Two levels: the generated SQL is asserted against the real
/// SQL Server provider without a connection, and the behaviour is exercised against SQLite in process
/// memory, where the mapping — including the value-converted room name — really has to work.
/// </summary>
public sealed class ChatRoomRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = TestDatabase.OpenInMemoryDatabase();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void RoomByNameQuery_FiltersInTheDatabase_AndSelectsOnlyWhatIsRendered()
    {
        using ChatDbContext context = TestDatabase.CreateContext();
        RoomName name = RoomName.Create(ChatRoomConstants.DefaultRoomName).Value;

        string sql = ChatRoomRepository.RoomByNameQuery(context, name).ToQueryString();

        sql.Should().Contain("WHERE [c].[Name] = ", "the unique index must serve the lookup, not a table scan in memory");
        sql.Should().Contain("[c].[Id]").And.Contain("[c].[Name]");
        sql.Should().NotContain("[c].[CreatedAtUtc]", "nothing renders the creation instant");
        sql.Should().NotContain("SELECT *");
    }

    [Fact]
    public async Task FindByNameAsync_ExistingRoom_ReturnsItsIdentifierAndName()
    {
        ChatRoom room = await AddRoomAsync(ChatRoomConstants.DefaultRoomName);

        ChatRoomDto? found = await FindAsync(ChatRoomConstants.DefaultRoomName);

        found.Should().NotBeNull();
        found!.Id.Should().Be(room.Id.Value);
        found.Name.Should().Be(ChatRoomConstants.DefaultRoomName);
    }

    [Fact]
    public async Task FindByNameAsync_UnknownName_ReturnsNull()
    {
        await AddRoomAsync(ChatRoomConstants.DefaultRoomName);

        ChatRoomDto? found = await FindAsync("Trading floor");

        found.Should().BeNull("an unseeded or renamed room is an expected outcome, not an exception");
    }

    [Fact]
    public async Task FindByNameAsync_DifferentlySpacedName_MatchesTheStoredRoom()
    {
        await AddRoomAsync("Trading floor");

        ChatRoomDto? found = await FindAsync("  Trading   floor  ");

        found.Should().NotBeNull("the value object normalises before the lookup, so both spellings are one name");
    }

    [Fact]
    public void AllRoomsQuery_OrdersInTheDatabase_AndSelectsOnlyWhatIsRendered()
    {
        using ChatDbContext context = TestDatabase.CreateContext();

        string sql = ChatRoomRepository.AllRoomsQuery(context).ToQueryString();

        sql.Should().Contain("ORDER BY [c].[Name]", "the picker's order is the database's job, not a sort in memory");
        sql.Should().Contain("[c].[Id]").And.Contain("[c].[Name]");
        sql.Should().NotContain("[c].[CreatedAtUtc]", "nothing renders the creation instant");
        sql.Should().NotContain("SELECT *");
    }

    /// <summary>
    /// Ordered by name so the picker does not reshuffle as rooms are created. Asserted against a real
    /// database rather than a stub, because it is the provider that does the sorting.
    /// </summary>
    [Fact]
    public async Task ListAsync_SeveralRooms_ReturnsThemAllOrderedByName()
    {
        await AddRoomAsync("Trading floor");
        await AddRoomAsync(ChatRoomConstants.DefaultRoomName);
        await AddRoomAsync("Alerts");

        IReadOnlyList<ChatRoomDto> rooms = await ListAsync();

        rooms.Select(room => room.Name).Should().Equal("Alerts", ChatRoomConstants.DefaultRoomName, "Trading floor");
        rooms.Should().OnlyContain(room => room.Id != Guid.Empty, "the picker joins each room by its id");
    }

    [Fact]
    public async Task ListAsync_UnseededDatabase_ReturnsAnEmptyList()
    {
        IReadOnlyList<ChatRoomDto> rooms = await ListAsync();

        rooms.Should().BeEmpty("no rooms is an expected state, not an error");
    }

    [Fact]
    public async Task FindByNameAsync_NullName_Throws()
    {
        await using ChatDbContext context = TestDatabase.CreateInMemoryContext(_connection);
        ChatRoomRepository repository = new(context);

        Func<Task> finding = () => repository.FindByNameAsync(null!, CancellationToken.None);

        await finding.Should().ThrowAsync<ArgumentNullException>();
    }

    private async Task<ChatRoom> AddRoomAsync(string name)
    {
        await using ChatDbContext context = TestDatabase.CreateInMemoryContext(_connection);

        ChatRoom room = ChatRoom.Create(RoomName.Create(name).Value, CreatedAt).Value;
        context.ChatRooms.Add(room);
        await context.SaveChangesAsync(CancellationToken.None);

        return room;
    }

    private async Task<IReadOnlyList<ChatRoomDto>> ListAsync()
    {
        await using ChatDbContext context = TestDatabase.CreateInMemoryContext(_connection);
        ChatRoomRepository repository = new(context);

        return await repository.ListAsync(CancellationToken.None);
    }

    private async Task<ChatRoomDto?> FindAsync(string name)
    {
        await using ChatDbContext context = TestDatabase.CreateInMemoryContext(_connection);
        ChatRoomRepository repository = new(context);

        return await repository.FindByNameAsync(RoomName.Create(name).Value, CancellationToken.None);
    }
}
