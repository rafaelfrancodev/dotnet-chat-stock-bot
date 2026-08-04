using Chat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
    /// The design-time model — the same one the migration is generated from. The runtime model is
    /// trimmed of the configuration a running application never reads (index sort order, for example),
    /// so assertions about the schema have to come from here.
    /// </summary>
    public static IModel ModelOf(ChatDbContext context) => context.GetService<IDesignTimeModel>().Model;
}
