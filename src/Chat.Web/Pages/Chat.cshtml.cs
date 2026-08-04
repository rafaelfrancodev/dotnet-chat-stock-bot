using System.Security.Claims;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages;

/// <summary>
/// The chat window. Deliberately minimal for now: it exists so authentication has a protected landing
/// point, and task 1.13 fills it with the message history and the SignalR client.
/// </summary>
/// <remarks>
/// <c>[Authorize]</c> is what makes an anonymous visitor land on the login page instead of the chat.
/// The participant's name comes from the authentication cookie, never from a query string or a form
/// field, which is the same rule the hub follows in task 1.12.
/// </remarks>
[Authorize]
public sealed class ChatModel : PageModel
{
    /// <summary>The signed-in participant's display name, taken from their claims.</summary>
    public string DisplayName =>
        User.FindFirstValue(ChatClaimTypes.DisplayName) ?? User.Identity?.Name ?? string.Empty;
}
