using System.Security.Claims;
using Chat.Application.Contracts.Rooms;
using Chat.Application.Features.Rooms.GetDefaultRoom;
using Chat.Domain.Common;
using Chat.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages;

/// <summary>
/// The chat window: it renders the room to join and loads the SignalR client. Everything else — the
/// history, the live posts and the sending — happens over the hub, so this page issues exactly one
/// query per request and never polls.
/// </summary>
/// <remarks>
/// <c>[Authorize]</c> is what makes an anonymous visitor land on the login page instead of the chat, and
/// the participant's name comes from the authentication cookie, never from a query string or a form
/// field — the same rule the hub follows.
/// <para>
/// The room is resolved through <see cref="GetDefaultRoomQuery"/> rather than by reading the database
/// here: a page is a host concern and must not know about <c>ChatDbContext</c>. The identifier it
/// renders is also the only room the browser can name, so a client cannot post into a room it was never
/// given.
/// </para>
/// </remarks>
/// <param name="sender">Dispatches the room lookup. The page owns no business logic of its own.</param>
[Authorize]
public sealed class ChatModel(ISender sender) : PageModel
{
    /// <summary>The signed-in participant's display name, taken from their claims.</summary>
    public string DisplayName =>
        User.FindFirstValue(ChatClaimTypes.DisplayName) ?? User.Identity?.Name ?? string.Empty;

    /// <summary>
    /// The room this window opens on, or <see langword="null"/> when the database holds none — which
    /// only happens if startup seeding never ran. The page then explains itself instead of opening a
    /// connection that could not join anything.
    /// </summary>
    public ChatRoomDto? Room { get; private set; }

    /// <summary>Resolves the room to render.</summary>
    /// <param name="cancellationToken">Bound to <c>HttpContext.RequestAborted</c> by the framework.</param>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result<ChatRoomDto> room = await sender
            .Send(new GetDefaultRoomQuery(), cancellationToken)
            .ConfigureAwait(false);

        Room = room.IsSuccess ? room.Value : null;
    }
}
