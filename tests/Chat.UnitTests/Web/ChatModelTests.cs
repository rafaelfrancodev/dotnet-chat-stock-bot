using System.Security.Claims;
using Chat.Application.Contracts.Rooms;
using Chat.Application.Errors;
using Chat.Application.Features.Rooms.GetDefaultRoom;
using Chat.Application.Features.Rooms.ListRooms;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Infrastructure.Identity;
using Chat.Web.Pages;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NSubstitute;

namespace Chat.UnitTests.Web;

/// <summary>
/// The chat page renders three things the client is then bound by: the rooms it may join, the one it opens
/// on, and the name it posts under. All must come from the server — the rooms from use cases, the name from
/// the authentication cookie — and none may be taken from the request.
/// </summary>
public sealed class ChatModelTests
{
    private const string DisplayName = "Alice Anderson";
    private const string UserName = "alice@example.com";

    private static readonly ChatRoomDto SeededRoom =
        new(Guid.CreateVersion7(), ChatRoomConstants.DefaultRoomName);

    private static readonly ChatRoomDto OtherRoom = new(Guid.CreateVersion7(), "Trading");

    private readonly ISender _sender = Substitute.For<ISender>();

    [Fact]
    public async Task OnGetAsync_SeededRoom_ExposesTheRoomThePageRenders()
    {
        RoomLookupReturns(Result.Success(SeededRoom));

        ChatModel page = new(_sender);
        await page.OnGetAsync(CancellationToken.None);

        page.Room.Should().Be(SeededRoom);
    }

    /// <summary>
    /// The picker's contents. Every room the query returns is offered, in the order it returned them —
    /// the page does not re-sort, because ordering is the repository's decision and duplicating it here
    /// would be a second place for it to drift.
    /// </summary>
    [Fact]
    public async Task OnGetAsync_SeveralRooms_ExposesThemAllInTheOrderTheQueryReturned()
    {
        RoomLookupReturns(Result.Success(SeededRoom));
        RoomListReturns(Result.Success<IReadOnlyList<ChatRoomDto>>([OtherRoom, SeededRoom]));

        ChatModel page = new(_sender);
        await page.OnGetAsync(CancellationToken.None);

        page.Rooms.Should().Equal(OtherRoom, SeededRoom);
    }

    /// <summary>
    /// The landing room is resolved by name, not taken as the first of the list. Rooms are ordered by name,
    /// so "first" would move as rooms are created and a new room called "Alerts" would silently become
    /// everybody's landing room.
    /// </summary>
    [Fact]
    public async Task OnGetAsync_ARoomSortsBeforeTheSeededOne_StillOpensOnTheSeededRoom()
    {
        RoomLookupReturns(Result.Success(SeededRoom));
        RoomListReturns(Result.Success<IReadOnlyList<ChatRoomDto>>([OtherRoom, SeededRoom]));

        ChatModel page = new(_sender);
        await page.OnGetAsync(CancellationToken.None);

        page.Room.Should().Be(SeededRoom);
        page.Rooms[0].Should().Be(OtherRoom, "the list is not the landing room");
    }

    [Fact]
    public async Task OnGetAsync_UnseededDatabase_ExposesNoRoomInsteadOfFailing()
    {
        RoomLookupReturns(Result.Failure<ChatRoomDto>(ChatRoomErrors.NotFound));

        ChatModel page = new(_sender);
        Func<Task> loading = () => page.OnGetAsync(CancellationToken.None);

        await loading.Should().NotThrowAsync();
        page.Room.Should().BeNull("the page explains itself rather than opening a connection to nothing");
        page.Rooms.Should().BeEmpty();
    }

    /// <summary>A failed listing must not take the page down; it renders a picker with nothing in it.</summary>
    [Fact]
    public async Task OnGetAsync_RoomListingFails_ExposesAnEmptyListInsteadOfFailing()
    {
        RoomLookupReturns(Result.Success(SeededRoom));
        RoomListReturns(Result.Failure<IReadOnlyList<ChatRoomDto>>(ChatRoomErrors.NotFound));

        ChatModel page = new(_sender);
        Func<Task> loading = () => page.OnGetAsync(CancellationToken.None);

        await loading.Should().NotThrowAsync();
        page.Rooms.Should().BeEmpty();
    }

    [Fact]
    public async Task OnGetAsync_Always_ForwardsTheRequestCancellationToken()
    {
        using CancellationTokenSource cancellation = new();
        RoomLookupReturns(Result.Success(SeededRoom));

        await new ChatModel(_sender).OnGetAsync(cancellation.Token);

        await _sender.Received(1).Send(Arg.Any<GetDefaultRoomQuery>(), cancellation.Token);
        await _sender.Received(1).Send(Arg.Any<ListRoomsQuery>(), cancellation.Token);
    }

    [Fact]
    public void DisplayName_SignedInParticipant_ComesFromTheClaimsAndNotTheRequest()
    {
        ChatModel page = PageFor(Principal(DisplayName));

        page.DisplayName.Should().Be(DisplayName);
    }

    [Fact]
    public void DisplayName_TicketWithoutADisplayNameClaim_FallsBackToTheUserName()
    {
        ChatModel page = PageFor(Principal(displayName: null));

        page.DisplayName.Should().Be(UserName);
    }

    [Fact]
    public void Page_RequiresAnAuthenticatedVisitor()
    {
        typeof(ChatModel).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Should().ContainSingle("an anonymous visitor must be sent to the login page, not to the chat");
    }

    private void RoomLookupReturns(Result<ChatRoomDto> result)
    {
        _sender.Send(Arg.Any<GetDefaultRoomQuery>(), Arg.Any<CancellationToken>()).Returns(result);

        // Defaulted so a test about the landing room does not have to stub the listing as well. Tests that
        // are about the list overwrite this.
        RoomListReturns(Result.Success<IReadOnlyList<ChatRoomDto>>([]));
    }

    private void RoomListReturns(Result<IReadOnlyList<ChatRoomDto>> result) =>
        _sender.Send(Arg.Any<ListRoomsQuery>(), Arg.Any<CancellationToken>()).Returns(result);

    private ChatModel PageFor(ClaimsPrincipal user) =>
        new(_sender)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = user } },
        };

    private static ClaimsPrincipal Principal(string? displayName)
    {
        List<Claim> claims = [new(ClaimTypes.Name, UserName)];

        if (displayName is not null)
        {
            claims.Add(new Claim(ChatClaimTypes.DisplayName, displayName));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
