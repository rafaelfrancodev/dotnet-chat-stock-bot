using System.Net;
using Chat.IntegrationTests.Infrastructure;
using Chat.Web.Hubs;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chat.IntegrationTests;

/// <summary>
/// "Registered users log in and talk in a chatroom" — the first half of that sentence, end to end over the
/// shipped Identity pages and the shipped authorisation.
/// </summary>
/// <param name="fixture">The running application and its throwaway database.</param>
[Collection(ChatServerCollection.Name)]
public sealed class AuthenticationFlowTests(ChatServerFixture fixture)
{
    private const string ChatPath = "/Chat";
    private const string LoginPath = "/Identity/Account/Login";

    [DockerFact]
    public async Task ChatPage_AnonymousVisitor_IsRedirectedToTheLoginPage()
    {
        using HttpClient browser = fixture.CreateAnonymousClient();

        using HttpResponseMessage response = await browser.GetAsync(ChatPath);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.Should().Contain(
            $"{LoginPath}?ReturnUrl=%2FChat",
            "an unauthenticated visitor must be asked to sign in and then sent back to the chat");
    }

    [DockerFact]
    public async Task RegisterThenLogIn_NewParticipant_ReachesTheChatPage()
    {
        using ChatParticipant registration = fixture.NewParticipant("Alice Anderson");

        using HttpResponseMessage registered = await registration.RegisterAsync();

        registered.StatusCode.Should().Be(HttpStatusCode.Redirect);
        registered.Headers.Location?.OriginalString.Should().Be(ChatPath);

        // A second, cookie-less browser for the same account, so the login page is genuinely exercised:
        // registering already signed the first one in.
        using ChatParticipant returning = fixture.NewSessionFor(registration);

        using HttpResponseMessage loggedIn = await returning.LogInAsync();

        loggedIn.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using HttpResponseMessage chatPage = await returning.Http.GetAsync(ChatPath);
        string html = await chatPage.Content.ReadAsStringAsync();

        chatPage.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("data-room-id=", "the page must hand the browser a room to join");
        html.Should().Contain(registration.DisplayName, "the display name captured at registration is rendered");
    }

    [DockerFact]
    public async Task HubNegotiate_WithoutAnAuthenticationCookie_IsUnauthorized()
    {
        using HttpClient browser = fixture.CreateAnonymousClient();
        using StringContent nothing = new(string.Empty);

        using HttpResponseMessage response = await browser.PostAsync(
            $"{ChatHub.Route}/negotiate?negotiateVersion=1",
            nothing);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [DockerFact]
    public async Task HubConnection_WithoutAnAuthenticationCookie_IsRejected()
    {
        await using HubConnection anonymous = fixture.BuildHubConnection(cookieHeader: null);

        Func<Task> connect = () => anonymous.StartAsync();

        await connect.Should().ThrowAsync<HttpRequestException>(
            "the hub carries [Authorize], so an anonymous connection must never be established");
        anonymous.State.Should().Be(HubConnectionState.Disconnected);
    }
}
