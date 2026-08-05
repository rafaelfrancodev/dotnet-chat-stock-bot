using System.Security.Claims;
using Chat.Application.Contracts.Rooms;
using Chat.Application.Errors;
using Chat.Application.Features.Rooms.GetDefaultRoom;
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
/// The chat page renders two things the client is then bound by: the room it may join and the name it
/// posts under. Both must come from the server — the room from a use case, the name from the
/// authentication cookie — and neither may be taken from the request.
/// </summary>
public sealed class ChatModelTests
{
    private const string DisplayName = "Alice Anderson";
    private const string UserName = "alice@example.com";

    private static readonly ChatRoomDto SeededRoom =
        new(Guid.CreateVersion7(), ChatRoomConstants.DefaultRoomName);

    private readonly ISender _sender = Substitute.For<ISender>();

    [Fact]
    public async Task OnGetAsync_SeededRoom_ExposesTheRoomThePageRenders()
    {
        RoomLookupReturns(Result.Success(SeededRoom));

        ChatModel page = new(_sender);
        await page.OnGetAsync(CancellationToken.None);

        page.Room.Should().Be(SeededRoom);
    }

    [Fact]
    public async Task OnGetAsync_UnseededDatabase_ExposesNoRoomInsteadOfFailing()
    {
        RoomLookupReturns(Result.Failure<ChatRoomDto>(ChatRoomErrors.NotFound));

        ChatModel page = new(_sender);
        Func<Task> loading = () => page.OnGetAsync(CancellationToken.None);

        await loading.Should().NotThrowAsync();
        page.Room.Should().BeNull("the page explains itself rather than opening a connection to nothing");
    }

    [Fact]
    public async Task OnGetAsync_Always_ForwardsTheRequestCancellationToken()
    {
        using CancellationTokenSource cancellation = new();
        RoomLookupReturns(Result.Success(SeededRoom));

        await new ChatModel(_sender).OnGetAsync(cancellation.Token);

        await _sender.Received(1).Send(Arg.Any<GetDefaultRoomQuery>(), cancellation.Token);
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

    private void RoomLookupReturns(Result<ChatRoomDto> result) =>
        _sender.Send(Arg.Any<GetDefaultRoomQuery>(), Arg.Any<CancellationToken>()).Returns(result);

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
