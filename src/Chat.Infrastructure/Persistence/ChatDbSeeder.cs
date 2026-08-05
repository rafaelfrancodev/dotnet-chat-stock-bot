using Chat.Application.Abstractions.Time;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chat.Infrastructure.Persistence;

/// <summary>
/// Brings the database to the state a host needs before it serves its first request: the schema is
/// migrated and the default room exists. Both steps are idempotent, so running them on every boot —
/// and running them from two instances at the same time — is safe.
/// </summary>
/// <remarks>
/// This lives next to the <see cref="ChatDbContext"/> rather than in a host, so <c>Chat.Bot</c>, which
/// never calls <c>AddPersistence</c>, cannot reach it. The room is created through
/// <see cref="ChatRoom.Create"/> and the injected clock, never through raw SQL: seeded data has to obey
/// the same invariants as data a user creates.
/// </remarks>
/// <param name="context">The chat database context, scoped to this initialisation.</param>
/// <param name="clock">Supplies the creation instant, because the domain never reads the clock itself.</param>
/// <param name="logger">Records what startup actually did to the database.</param>
public sealed class ChatDbSeeder(ChatDbContext context, IDateTimeProvider clock, ILogger<ChatDbSeeder> logger)
{
    /// <summary>
    /// Name of the room every participant lands in. The challenge needs exactly one room; the
    /// multiple-rooms bonus adds more next to it rather than replacing it. The literal itself lives in
    /// the domain, because the chat page resolves the same room by the same name — two copies could
    /// drift and leave the window with nothing to join.
    /// </summary>
    public const string DefaultRoomName = ChatRoomConstants.DefaultRoomName;

    /// <summary>Applies pending migrations, then seeds the default room.</summary>
    /// <param name="cancellationToken">Cancels the startup work, e.g. when the host is shutting down.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await ApplyMigrationsAsync(cancellationToken).ConfigureAwait(false);
        await SeedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies every migration the database is missing, and logs which ones — a host that silently
    /// rewrites a schema is worse than one that fails. Equivalent to <c>dotnet ef database update</c>,
    /// which stays the documented path for anyone who would rather migrate before starting the app.
    /// </summary>
    /// <param name="cancellationToken">Cancels the migration.</param>
    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken = default)
    {
        string[] pending =
        [
            .. await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false),
        ];

        if (pending.Length == 0)
        {
            logger.LogInformation("Chat database schema is up to date; no migration applied.");
            return;
        }

        logger.LogInformation(
            "Applying {PendingMigrationCount} pending migration(s) to the chat database: {PendingMigrations}.",
            pending.Length,
            string.Join(", ", pending));

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the default room exists. Safe to call on every boot: the room is looked up first, and if
    /// a concurrently starting instance wins the insert, the unique index on <c>ChatRooms.Name</c>
    /// rejects the duplicate and this method treats that as success.
    /// </summary>
    /// <param name="cancellationToken">Cancels the lookup and the insert.</param>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        RoomName name = DefaultRoomNameValue();

        if (await RoomExistsAsync(name, cancellationToken).ConfigureAwait(false))
        {
            logger.LogDebug("Default chat room {RoomName} already exists; nothing seeded.", DefaultRoomName);
            return;
        }

        ChatRoom room = ChatRoom.Create(name, clock.UtcNow).Value;
        context.ChatRooms.Add(room);

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            // The lookup above is a read, so two hosts starting together can both pass it. Only the
            // unique index can settle that race; losing it means the room now exists, which is the
            // outcome this method was asked for. Anything else is a real failure and must surface.
            context.Entry(room).State = EntityState.Detached;

            if (!await RoomExistsAsync(name, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            logger.LogInformation(
                "Default chat room {RoomName} was created concurrently by another instance ({Reason}).",
                DefaultRoomName,
                exception.InnerException?.Message ?? exception.Message);

            return;
        }

        logger.LogInformation(
            "Seeded the default chat room {RoomName} ({ChatRoomId}).",
            DefaultRoomName,
            room.Id.Value);
    }

    /// <summary>An existence query, not a load: the answer is a boolean and the index on Name serves it.</summary>
    private Task<bool> RoomExistsAsync(RoomName name, CancellationToken cancellationToken) =>
        context.ChatRooms.AsNoTracking().AnyAsync(room => room.Name == name, cancellationToken);

    /// <summary>
    /// The default name as a validated value object. <see cref="DefaultRoomName"/> is a compile-time
    /// constant well inside every rule, so a failure here would mean the room name invariants changed
    /// underneath the seeder — a bug, not an expected outcome.
    /// </summary>
    private static RoomName DefaultRoomNameValue()
    {
        Result<RoomName> name = RoomName.Create(DefaultRoomName);

        return name.IsSuccess
            ? name.Value
            : throw new InvalidOperationException(
                $"The default chat room name \"{DefaultRoomName}\" is no longer a valid room name ({name.Error}).");
    }
}
