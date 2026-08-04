using Chat.Infrastructure.Identity;
using Chat.Infrastructure.Persistence;

namespace Chat.Web.Identity;

/// <summary>
/// Registers ASP.NET Core Identity with its default UI: registration, login and logout are framework
/// pages, which is exactly the amount of frontend this challenge asks for.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Wires Identity over <see cref="ChatDbContext"/> and hardens the authentication cookie.
    /// </summary>
    /// <param name="services">Service collection to register into.</param>
    /// <param name="requireSecureCookie">
    /// <see langword="true"/> outside Development, which pins the cookie to HTTPS
    /// (<see cref="CookieSecurePolicy.Always"/>). In Development the policy follows the request, because
    /// the documented local run serves plain HTTP on port 5271 and an <c>Always</c> cookie would never
    /// be sent back — every login would silently appear to fail.
    /// </param>
    public static IServiceCollection AddChatIdentity(this IServiceCollection services, bool requireSecureCookie)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddDefaultIdentity<ApplicationUser>(options =>
                // The password policy and the lockout settings are deliberately left at their
                // framework defaults. Only account confirmation is pinned off: this deliverable ships
                // no email sender, so requiring a confirmation nobody can deliver would lock every
                // registration out of the chat.
                options.SignIn.RequireConfirmedAccount = false)
            .AddEntityFrameworkStores<ChatDbContext>()
            .AddClaimsPrincipalFactory<DisplayNameClaimsPrincipalFactory>();

        services.ConfigureApplicationCookie(options =>
        {
            // Not readable from script, so an XSS bug cannot become a stolen session.
            options.Cookie.HttpOnly = true;

            // Lax still sends the cookie on a top-level navigation back from the login page, while
            // keeping it off cross-site POSTs — the CSRF vector antiforgery tokens also cover.
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = requireSecureCookie
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
        });

        return services;
    }
}
