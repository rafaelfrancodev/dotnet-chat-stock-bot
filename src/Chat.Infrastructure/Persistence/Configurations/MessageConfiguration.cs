using Chat.Domain.Messages;
using Chat.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Persistence.Configurations;

/// <summary>Maps the <see cref="Message"/> aggregate onto the <c>Messages</c> table.</summary>
internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Messages");

        builder.HasKey(message => message.Id);

        // Ids are version-7 GUIDs created by the domain factory; the database never invents one.
        builder.Property(message => message.Id).ValueGeneratedNever();

        // Cross-aggregate reference, deliberately a plain column with no foreign key and no navigation:
        // ChatRoom and Message are separate consistency boundaries, a room owns no message collection,
        // and an unknown room is answered as a Result failure by IChatRoomRepository.ExistsAsync before
        // the insert rather than as a DbUpdateException after it.
        builder.Property(message => message.ChatRoomId).IsRequired();

        builder.ComplexProperty(message => message.Author, author =>
        {
            // No foreign key to AspNetUsers on purpose: the bot owns its posts and "system:bot" is not
            // a registered user, so a foreign key here would reject every quote answer the challenge
            // requires. The value is written from the caller's claims, never from a client payload.
            author.Property(value => value.UserId)
                .HasColumnName("AuthorUserId")
                .HasMaxLength(PersistenceConstants.UserIdMaxLength)
                .IsRequired();

            author.Property(value => value.DisplayName)
                .HasColumnName("AuthorDisplayName")
                .HasMaxLength(PersistenceConstants.DisplayNameMaxLength)
                .IsRequired();

            // Derived from UserId; nothing to store.
            author.Ignore(value => value.IsBot);
        });

        builder.Property(message => message.Content)
            .HasConversion<MessageContentConverter>()
            .HasColumnName("Content")
            .HasMaxLength(MessageConstants.MaxContentLength)
            .IsRequired();

        // Explicit enum values exist precisely so the stored number is part of the contract.
        builder.Property(message => message.Origin)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(message => message.PostedAtUtc)
            .HasConversion<UtcDateTimeOffsetConverter>()
            .HasColumnType("datetime2(7)")
            .IsRequired();

        // The one query this table exists to serve: "last 50 of a room, newest first". Leading room
        // column plus a descending timestamp makes it a single index range scan whose cost does not
        // grow with history.
        builder.HasIndex(message => new { message.ChatRoomId, message.PostedAtUtc })
            .HasDatabaseName("IX_Messages_ChatRoomId_PostedAtUtc")
            .IsDescending(false, true);

        builder.Ignore(message => message.DomainEvents);
    }
}
