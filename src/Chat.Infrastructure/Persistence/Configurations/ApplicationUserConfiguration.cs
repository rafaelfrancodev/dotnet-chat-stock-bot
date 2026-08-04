using Chat.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Persistence.Configurations;

/// <summary>
/// Refines Identity's <c>AspNetUsers</c> mapping with the one column this application adds.
/// </summary>
internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Bounded, not nvarchar(max): the value is copied into Messages.AuthorDisplayName on every
        // post, so the source column must not be able to overflow the destination.
        builder.Property(user => user.DisplayName)
            .HasMaxLength(PersistenceConstants.DisplayNameMaxLength)
            .IsRequired();
    }
}
