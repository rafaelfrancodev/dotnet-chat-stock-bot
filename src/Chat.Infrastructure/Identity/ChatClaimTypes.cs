namespace Chat.Infrastructure.Identity;

/// <summary>
/// Claim types this application adds on top of Identity's own. Declared once so the code that issues a
/// claim and the code that reads it cannot disagree about its name.
/// </summary>
public static class ChatClaimTypes
{
    /// <summary>
    /// The name rendered next to a post. Issued at sign-in by
    /// <see cref="DisplayNameClaimsPrincipalFactory"/> so the chat surface can read the author's name
    /// from the authentication cookie instead of querying <c>AspNetUsers</c> on every message.
    /// </summary>
    public const string DisplayName = "display_name";
}
