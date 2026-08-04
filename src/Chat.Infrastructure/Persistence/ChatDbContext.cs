using Chat.Domain.ChatRooms;
using Chat.Domain.Messages;
using Chat.Infrastructure.Identity;
using Chat.Infrastructure.Persistence.Converters;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Persistence;

/// <summary>
/// The single database context: chat aggregates and ASP.NET Core Identity share it, so there is one
/// connection pool, one migration history and one transaction scope.
/// </summary>
/// <remarks>
/// Aggregates are materialised through their private parameterless constructors and written through
/// their backing fields, which is why no configuration here switches to
/// <see cref="Microsoft.EntityFrameworkCore.PropertyAccessMode.Property"/>: the domain's
/// <c>private init</c> properties exist to keep the factories the only public way to build a message
/// or a room, and the mapping must not need a second way in.
/// </remarks>
/// <param name="options">Provider and connection configuration supplied by the composition root.</param>
public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    /// <summary>The rooms participants chat in.</summary>
    public DbSet<ChatRoom> ChatRooms => Set<ChatRoom>();

    /// <summary>
    /// Posted messages — participants' and the bot's answers. A <c>/stock=</c> command is a command and
    /// never reaches this set; see <c>PostMessageHandler</c>.
    /// </summary>
    public DbSet<Message> Messages => Set<Message>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity's own mapping first, then ours; ApplicationUserConfiguration refines AspNetUsers.
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChatDbContext).Assembly);
    }

    /// <inheritdoc/>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Strongly-typed ids are converted once, by convention, so every mapping that mentions one
        // (a key here, a cross-aggregate reference there) cannot disagree about the column type.
        configurationBuilder.Properties<ChatRoomId>().HaveConversion<ChatRoomIdConverter>();
        configurationBuilder.Properties<MessageId>().HaveConversion<MessageIdConverter>();
    }
}
