using Chat.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Chat.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Builds a <see cref="ChatDbContext"/> against the real SQL Server provider without ever opening a
/// connection. Everything these tests assert — the model, the generated SQL — is produced by the
/// provider at build time, so they stay as fast and deterministic as any other unit test while still
/// checking the mapping that will actually run in production.
/// </summary>
internal static class TestDatabase
{
    /// <summary>A syntactically valid connection string pointing at nothing. Never dialled.</summary>
    private const string UnusedConnectionString =
        "Server=127.0.0.1,1433;Database=ChatDb;User Id=sa;Password=not-used;Encrypt=False";

    public static ChatDbContext CreateContext()
    {
        DbContextOptions<ChatDbContext> options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseSqlServer(UnusedConnectionString)
            .Options;

        return new ChatDbContext(options);
    }

    /// <summary>
    /// An empty database in process memory, created from the same mapping, over a real relational
    /// provider — so a unique index is genuinely enforced. Used by the seeder tests, which have to
    /// execute queries rather than only inspect metadata. The connection is the caller's to dispose:
    /// the database lives exactly as long as it stays open.
    /// </summary>
    /// <param name="connection">An open SQLite connection, shared by every context in one test.</param>
    /// <param name="interceptors">Interceptors used to simulate what a second host would do concurrently.</param>
    public static ChatDbContext CreateInMemoryContext(SqliteConnection connection, params IInterceptor[] interceptors)
    {
        DbContextOptions<ChatDbContext> options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptors)
            .Options;

        return new ChatDbContext(options);
    }

    /// <summary>Opens the connection and creates the schema from the model.</summary>
    public static SqliteConnection OpenInMemoryDatabase()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();

        using ChatDbContext context = CreateInMemoryContext(connection);
        context.Database.EnsureCreated();

        return connection;
    }

    /// <summary>
    /// The design-time model — the same one the migration is generated from. The runtime model is
    /// trimmed of the configuration a running application never reads (index sort order, for example),
    /// so assertions about the schema have to come from here.
    /// </summary>
    public static IModel ModelOf(ChatDbContext context) => context.GetService<IDesignTimeModel>().Model;
}
