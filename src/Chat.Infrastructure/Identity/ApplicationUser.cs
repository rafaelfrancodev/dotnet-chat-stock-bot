using Chat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace Chat.Infrastructure.Identity;

/// <summary>
/// A registered participant. Extends the framework user with the only field the chat itself needs: the
/// name rendered next to a post.
/// </summary>
/// <remarks>
/// The authentication identity lives here, but a posted message never points at this row: a message
/// stores its author's user id as a plain value, because the bot ("system:bot") owns posts too and is
/// not — and must not become — a registered user. See <c>MessageConfiguration</c>.
/// </remarks>
public sealed class ApplicationUser : IdentityUser
{
    /// <summary>
    /// Upper bound on <see cref="DisplayName"/>, published so the registration form can reject an
    /// over-long name with a validation message instead of letting the insert truncate or fail.
    /// </summary>
    public const int DisplayNameMaxLength = PersistenceConstants.DisplayNameMaxLength;

    /// <summary>
    /// Human-readable name shown as the post owner in the chat window. Bounded to the same length as
    /// Identity's own <c>UserName</c> so it always fits the message author column.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
}
