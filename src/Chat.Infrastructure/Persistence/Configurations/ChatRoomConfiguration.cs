using Chat.Domain.ChatRooms;
using Chat.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Persistence.Configurations;

/// <summary>Maps the <see cref="ChatRoom"/> aggregate onto the <c>ChatRooms</c> table.</summary>
internal sealed class ChatRoomConfiguration : IEntityTypeConfiguration<ChatRoom>
{
    public void Configure(EntityTypeBuilder<ChatRoom> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ChatRooms");

        builder.HasKey(room => room.Id);

        builder.Property(room => room.Id).ValueGeneratedNever();

        builder.Property(room => room.Name)
            .HasConversion<RoomNameConverter>()
            .HasColumnName("Name")
            .HasMaxLength(RoomName.MaxLength)
            .IsRequired();

        // Uniqueness is a database concern: an aggregate cannot see its peers, so only the index can
        // decide the race between two concurrent "create General" requests. Case sensitivity follows
        // the column collation (case-insensitive by default on SQL Server), which is stricter than
        // RoomName itself and therefore safe.
        builder.HasIndex(room => room.Name)
            .IsUnique()
            .HasDatabaseName("IX_ChatRooms_Name");

        builder.Property(room => room.CreatedAtUtc)
            .HasConversion<UtcDateTimeOffsetConverter>()
            .HasColumnType("datetime2(7)")
            .IsRequired();

        builder.Ignore(room => room.DomainEvents);
    }
}
