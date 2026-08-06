using System.Net;
using Chat.Domain.ChatRooms;
using Chat.Domain.Messages;
using Chat.Infrastructure.Identity;
using Chat.Infrastructure.Persistence;
using Chat.Web.Hubs;
using MassTransit.Testing;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Chat.IntegrationTests.Infrastructure;

/// <summary>
/// The whole environment the integration tests share: one throwaway SQL Server container, one in-memory
/// <c>Chat.Web</c> host pointed at it, and the in-memory bus that stands in for RabbitMQ.
/// </summary>
/// <remarks>
/// <b>Scoped to the collection, on purpose.</b> Starting a SQL Server container costs tens of seconds, so
/// one is started for <see cref="ChatServerCollection"/> and the schema is migrated once — by the host's
/// own <c>InitializeChatDatabaseAsync</c>, which is the production startup path rather than a test-only
/// copy of it. Every test in the collection therefore runs against the same database, serialised by xUnit.
/// <para>
/// <b>Isolation is by aggregate key, not by cleanup.</b> Each test creates its own chat room and its own
/// participant accounts, and every read path in the application filters by <c>ChatRoomId</c>. That is
/// exactly how the application partitions data, so the test that needs sixty messages to prove the
/// fifty-message cap cannot be seen by any other test — and no test has to truncate a table or drop a
/// database between runs.
/// </para>
/// <para>
/// Nothing is started when Docker is unreachable: every test is skipped by
/// <see cref="DockerFactAttribute"/>, and a fixture that threw instead would fail the collection.
/// </para>
/// </remarks>
public sealed class ChatServerFixture : IAsyncLifetime
{
    /// <summary>
    /// The image <c>docker-compose.dev.yml</c> already uses, so the suite pulls nothing a developer who
    /// has run the application does not already have, and tests the provider production runs on.
    /// </summary>
    public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    /// <summary>How long the container is given to become usable before the suite gives up on it.</summary>
    private static readonly TimeSpan ContainerStartTimeout = TimeSpan.FromMinutes(5);

    private readonly MsSqlContainer? container;

    private ChatApplicationFactory? application;
    private ITestHarness? broker;

    /// <summary>Creates the fixture. The container is only built when Docker can run it.</summary>
    public ChatServerFixture() =>
        container = DockerEnvironment.IsAvailable
            ? new MsSqlBuilder(SqlServerImage).Build()
            : null;

    /// <summary>The running application: use it for clients, hub connections and service scopes.</summary>
    public ChatApplicationFactory Application => application ?? throw NotStarted();

    /// <summary>The in-memory bus that replaced RabbitMQ, for asserting what was published.</summary>
    public ITestHarness Broker => broker ?? throw NotStarted();

    /// <summary>Base address of the in-memory server, e.g. <c>http://localhost/</c>.</summary>
    public Uri BaseAddress => Application.ClientOptions.BaseAddress;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (container is null)
        {
            return;
        }

        // Bounded on purpose: a Docker daemon that accepts the create call and then never brings the
        // container up would otherwise hang the whole build with no output. Five minutes is far more than a
        // cached SQL Server image needs, and the failure names the way out.
        using CancellationTokenSource startup = new(ContainerStartTimeout);

        try
        {
            await container.StartAsync(startup.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            throw new InvalidOperationException(
                $"The SQL Server container did not start within {ContainerStartTimeout.TotalMinutes:0} minutes. "
                + "Check that Docker is healthy, or set "
                + $"{DockerEnvironment.SkipVariableName}=1 to skip the integration tests.",
                exception);
        }

        string connectionString = ChatDatabaseConnectionString(container);
        application = new ChatApplicationFactory(connectionString);

        // Touching Services builds and starts the host, which is where Chat.Web migrates and seeds the
        // database. Doing it here means the cost is paid once for the collection and no single test
        // carries it.
        broker = application.Services.GetRequiredService<ITestHarness>();

        // AddMassTransitTestHarness takes over the bus lifecycle: it replaces MassTransit's hosted
        // service, so starting the web host does NOT start the bus — the harness has to. Without this
        // call the bus never starts and IPublishEndpoint.Publish blocks forever waiting for it, which is
        // exactly the stall that only ever hit the three tests that publish (`SendMessage did not answer
        // within 30 seconds`) while the plain-message, history and authentication tests always passed.
        await broker.Start().ConfigureAwait(false);

        await GuardAgainstADeveloperDatabaseAsync(connectionString).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        // Stopped before the host is torn down, because the harness owns the bus rather than the host.
        if (broker is not null)
        {
            await broker.Stop().ConfigureAwait(false);
        }

        if (application is not null)
        {
            await application.DisposeAsync().ConfigureAwait(false);
        }

        if (container is not null)
        {
            await container.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>An unauthenticated browser: no cookies yet, and redirects are not followed.</summary>
    public HttpClient CreateAnonymousClient() => Application.CreateDefaultClient(new CookieJarHandler());

    /// <summary>
    /// A participant with a unique account name, not yet registered — for the test that walks the
    /// registration and login pages itself.
    /// </summary>
    /// <param name="displayName">Name the registration page captures and every post then renders.</param>
    public ChatParticipant NewParticipant(string displayName)
    {
        CookieJarHandler cookies = new();

        return new ChatParticipant(
            Application.CreateDefaultClient(cookies),
            cookies,
            BaseAddress,
            $"{Guid.NewGuid():N}@example.test",
            displayName);
    }

    /// <summary>Registers a participant through the real page, which also signs them in.</summary>
    /// <param name="displayName">Name every post of theirs will carry.</param>
    public async Task<ChatParticipant> RegisterParticipantAsync(string displayName)
    {
        ChatParticipant participant = NewParticipant(displayName);
        using HttpResponseMessage response = await participant.RegisterAsync().ConfigureAwait(false);

        if (response.StatusCode is not HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException(
                $"Registering {participant.Email} answered {(int)response.StatusCode}, not a redirect: "
                + await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        return participant;
    }

    /// <summary>
    /// The same account seen from a second browser: same credentials, empty cookie jar. Used to log in
    /// through the login page, since registering already signed the first session in.
    /// </summary>
    /// <param name="participant">An account that has already been registered.</param>
    public ChatParticipant NewSessionFor(ChatParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        CookieJarHandler cookies = new();

        return new ChatParticipant(
            Application.CreateDefaultClient(cookies),
            cookies,
            BaseAddress,
            participant.Email,
            participant.DisplayName);
    }

    /// <summary>The Identity user id of a registered participant, as the hub reads it from the ticket.</summary>
    /// <param name="participant">A registered participant.</param>
    public async Task<string> UserIdAsync(ChatParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        await using AsyncServiceScope scope = Application.Services.CreateAsyncScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser user = await users.FindByEmailAsync(participant.Email).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"{participant.Email} was never registered.");

        return user.Id;
    }

    /// <summary>
    /// Builds a SignalR connection to the in-memory server, authenticated with the given session cookie.
    /// </summary>
    /// <param name="cookieHeader">
    /// The <c>Cookie</c> header to send, or <see langword="null"/> for an anonymous connection.
    /// </param>
    /// <remarks>
    /// Long polling is the transport, because <c>TestServer</c>'s message handler speaks HTTP and not
    /// WebSockets. It changes nothing the tests assert: SignalR guarantees per-connection ordering on every
    /// transport, and the hub, the authorisation and the group fan-out are the shipped ones. The cookie
    /// travels as a header rather than in <c>HttpConnectionOptions.Cookies</c>, whose container is dropped
    /// as soon as the handler is supplied by <c>HttpMessageHandlerFactory</c>.
    /// </remarks>
    public HubConnection BuildHubConnection(string? cookieHeader) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(BaseAddress, ChatHub.Route), options =>
            {
                options.HttpMessageHandlerFactory = _ => Application.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;

                if (!string.IsNullOrEmpty(cookieHeader))
                {
                    options.Headers["Cookie"] = cookieHeader;
                }
            })
            .Build();

    /// <summary>Connects one participant to the hub and starts recording what the server pushes.</summary>
    /// <param name="participant">A registered, signed-in participant.</param>
    public Task<TestHubClient> ConnectAsync(ChatParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        return TestHubClient.StartAsync(BuildHubConnection(participant.CookieHeader));
    }

    /// <summary>Creates a room of its own for one test, through the domain factory.</summary>
    /// <param name="name">Room name; make it unique per test so the tests cannot see each other's posts.</param>
    /// <returns>The new room's identifier, as the hub receives it from a client.</returns>
    public async Task<Guid> CreateRoomAsync(string name)
    {
        await using AsyncServiceScope scope = Application.Services.CreateAsyncScope();
        ChatDbContext context = scope.ServiceProvider.GetRequiredService<ChatDbContext>();

        ChatRoom room = ChatRoom.Create(RoomName.Create(name).Value, DateTimeOffset.UtcNow).Value;
        context.ChatRooms.Add(room);
        await context.SaveChangesAsync().ConfigureAwait(false);

        return room.Id.Value;
    }

    /// <summary>
    /// Writes participant posts straight to the database, one second apart, so a test can set up more
    /// history than the fifty-message cap without sending a hundred hub messages.
    /// </summary>
    /// <param name="chatRoomId">Room the posts belong to.</param>
    /// <param name="count">How many posts to write.</param>
    /// <param name="firstPostedAtUtc">Timestamp of the first post; each following post is one second later.</param>
    /// <returns>The posts' contents, oldest first — the exact order a reader must see them in.</returns>
    public async Task<IReadOnlyList<string>> SeedParticipantHistoryAsync(
        Guid chatRoomId,
        int count,
        DateTimeOffset firstPostedAtUtc)
    {
        await using AsyncServiceScope scope = Application.Services.CreateAsyncScope();
        ChatDbContext context = scope.ServiceProvider.GetRequiredService<ChatDbContext>();

        MessageAuthor author = MessageAuthor.Create("seed:participant", "Seed Participant").Value;
        List<string> contents = [];

        for (int index = 0; index < count; index++)
        {
            string text = $"seeded line {index + 1}";
            contents.Add(text);

            context.Messages.Add(Message.PostByParticipant(
                new ChatRoomId(chatRoomId),
                author,
                MessageContent.Create(text).Value,
                firstPostedAtUtc.AddSeconds(index)).Value);
        }

        await context.SaveChangesAsync().ConfigureAwait(false);

        return contents;
    }

    /// <summary>Number of rows in one room, whatever their author or origin.</summary>
    /// <param name="chatRoomId">Room to count.</param>
    public async Task<int> CountMessagesAsync(Guid chatRoomId)
    {
        await using AsyncServiceScope scope = Application.Services.CreateAsyncScope();
        ChatDbContext context = scope.ServiceProvider.GetRequiredService<ChatDbContext>();

        ChatRoomId roomId = new(chatRoomId);

        return await context.Messages.CountAsync(message => message.ChatRoomId == roomId).ConfigureAwait(false);
    }

    /// <summary>
    /// The challenge's hard constraint, asked of the database in the plainest way possible: how many rows
    /// are there whose text is a slash command?
    /// </summary>
    /// <remarks>
    /// Deliberately raw SQL, and deliberately not scoped to a room: it is the same query a reviewer would
    /// run by hand (<c>SELECT COUNT(*) FROM Messages WHERE Content LIKE '/%'</c>) and the answer must be
    /// zero for the whole table, for every test that has run so far. EF Core could not express it anyway —
    /// <c>Content</c> is a value-converted type, so <c>message.Content.Value.StartsWith("/")</c> does not
    /// translate.
    /// </remarks>
    public async Task<int> CountCommandRowsAsync()
    {
        await using AsyncServiceScope scope = Application.Services.CreateAsyncScope();
        ChatDbContext context = scope.ServiceProvider.GetRequiredService<ChatDbContext>();

        return await context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM Messages WHERE Content LIKE '/%'")
            .SingleAsync()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The container's connection string, pointed at the application's own database rather than
    /// <c>master</c>. The schema itself is created by the host's startup migration.
    /// </summary>
    private static string ChatDatabaseConnectionString(MsSqlContainer container) =>
        container.GetConnectionString().Replace("Database=master", "Database=ChatDb", StringComparison.Ordinal);

    /// <summary>
    /// Proves the host resolved the container's database and not the developer's. Worth the two lines: the
    /// suite runs in the Development environment, so <c>Chat.Web</c>'s user secrets are loaded, and a
    /// mistake in how the connection string is supplied would silently migrate and write to the real
    /// <c>ChatDb</c> instead of failing.
    /// </summary>
    /// <param name="expected">The connection string the factory was given.</param>
    private async Task GuardAgainstADeveloperDatabaseAsync(string expected)
    {
        await using AsyncServiceScope scope = Application.Services.CreateAsyncScope();
        ChatDbContext context = scope.ServiceProvider.GetRequiredService<ChatDbContext>();

        SqlConnectionStringBuilder wanted = new(expected);
        SqlConnectionStringBuilder resolved = new(context.Database.GetConnectionString());

        // Compared field by field, and never as whole strings: only the server and the database matter, and
        // a password must not reach an assertion message.
        if (!string.Equals(resolved.DataSource, wanted.DataSource, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(resolved.InitialCatalog, wanted.InitialCatalog, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The test host is not using the throwaway container's database: it resolved "
                + $"\"{resolved.DataSource}/{resolved.InitialCatalog}\" instead of "
                + $"\"{wanted.DataSource}/{wanted.InitialCatalog}\". Configuration precedence changed, and "
                + "the suite would read and write a real database.");
        }
    }

    private static InvalidOperationException NotStarted() =>
        new("The chat server fixture was not started. Integration tests must be marked [DockerFact].");
}
