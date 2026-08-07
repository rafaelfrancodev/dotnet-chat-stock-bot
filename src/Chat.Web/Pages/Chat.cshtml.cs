using System.Security.Claims;
using Chat.Application.Contracts.Rooms;
using Chat.Application.Features.Rooms.GetDefaultRoom;
using Chat.Application.Features.Rooms.ListRooms;
using Chat.Domain.Common;
using Chat.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages;

/// <summary>
/// The chat window: it renders the rooms that can be joined, the one to open on, and loads the SignalR
/// client. Everything else — the history, the live posts, switching room and sending — happens over the
/// hub, so this page issues two queries per request and never polls.
/// </summary>
/// <remarks>
/// <c>[Authorize]</c> is what makes an anonymous visitor land on the login page instead of the chat, and
/// the participant's name comes from the authentication cookie, never from a query string or a form
/// field — the same rule the hub follows.
/// <para>
/// Rooms are resolved through the use-case pipeline rather than by reading the database here: a page is a
/// host concern and must not know about <c>ChatDbContext</c>. The rooms it renders are also the only ones
/// the browser can name — though naming another is not a way in, because posting re-checks the room and
/// joining only ever subscribes the caller's own connection.
/// </para>
/// <para>
/// The room to open on is the seeded one, resolved by name rather than taken as "the first in the list".
/// Once rooms are ordered by name, "first" would move as rooms are created, so a new room called "Alerts"
/// would silently become everybody's landing room.
/// </para>
/// </remarks>
/// <param name="sender">Dispatches the room queries. The page owns no business logic of its own.</param>
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

    /// <summary>
    /// Every room a participant can switch to, ordered by name. Contains <see cref="Room"/> whenever that
    /// is not <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<ChatRoomDto> Rooms { get; private set; } = [];

    /// <summary>Resolves the rooms to render.</summary>
    /// <param name="cancellationToken">Bound to <c>HttpContext.RequestAborted</c> by the framework.</param>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result<ChatRoomDto> room = await sender
            .Send(new GetDefaultRoomQuery(), cancellationToken)
            .ConfigureAwait(false);

        Room = room.IsSuccess ? room.Value : null;

        Result<IReadOnlyList<ChatRoomDto>> rooms = await sender
            .Send(new ListRoomsQuery(), cancellationToken)
            .ConfigureAwait(false);

        Rooms = rooms.IsSuccess ? rooms.Value : [];
    }
}
