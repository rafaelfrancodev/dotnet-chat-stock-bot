using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Identity;

/// <summary>
/// Adds <see cref="ChatClaimTypes.DisplayName"/> to the principal Identity builds at sign-in.
/// </summary>
/// <remarks>
/// Every post needs the author's display name, and the hub takes the author from
/// <c>Context.User</c> — never from the client payload. Carrying the name in the cookie turns that into
/// a lookup with no I/O at all, instead of one <c>AspNetUsers</c> query per message.
/// </remarks>
/// <param name="userManager">Identity's user manager, used by the base factory.</param>
/// <param name="optionsAccessor">Identity options, used by the base factory.</param>
public sealed class DisplayNameClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, optionsAccessor)
{
    /// <inheritdoc/>
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        ClaimsIdentity identity = await base.GenerateClaimsAsync(user).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            identity.AddClaim(new Claim(ChatClaimTypes.DisplayName, user.DisplayName));
        }

        return identity;
    }
}
