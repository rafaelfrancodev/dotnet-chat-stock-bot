using Chat.Domain.ChatRooms;
using Chat.Domain.Messages;
using Chat.Infrastructure.Persistence;
using Chat.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Chat.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Checks the one query the challenge grades on resource consumption — "the last 50 messages of a
/// room, ordered by timestamp" — by compiling it to SQL. No connection is opened: EF Core translates
/// the expression tree offline, which is exactly the step that would fail if the mapping stopped
/// supporting it.
/// </summary>
public sealed class MessageRepositoryQueryTests : IDisposable
{
    private readonly ChatDbContext _context = TestDatabase.CreateContext();

    public void Dispose() => _context.Dispose();

    [Fact]
    public void LatestMessagesQuery_FiltersOrdersAndLimits_InTheDatabase()
    {
        string sql = LatestMessagesSql();

        sql.Should().Contain("TOP(", "the limit must be applied by SQL Server, not after loading a history");
        sql.Should().Contain("WHERE [m].[ChatRoomId] = ", "only one room's posts may be read");
        sql.Should().Contain("ORDER BY [m].[PostedAtUtc] DESC", "timestamp ordering is a challenge requirement");
        sql.Should().Contain("[m].[Id] DESC", "equal timestamps still need a deterministic order");
    }

    [Fact]
    public void LatestMessagesQuery_SelectsOnlyTheColumnsTheChatWindowRenders()
    {
        string sql = LatestMessagesSql();

        sql.Should().Contain("[m].[Id]")
            .And.Contain("[m].[AuthorDisplayName]")
            .And.Contain("[m].[Content]")
            .And.Contain("[m].[PostedAtUtc]")
            .And.Contain("[m].[Origin]");

        sql.Should().NotContain("[m].[AuthorUserId]",
            "MessageDto carries no user id — broadcasting authentication ids to every browser leaks them for nothing");
        sql.Should().NotContain("SELECT *");
    }

    private string LatestMessagesSql() =>
        MessageRepository
            .LatestMessagesQuery(_context, ChatRoomId.New(), MessageConstants.LatestMessagesCount)
            .ToQueryString();
}
