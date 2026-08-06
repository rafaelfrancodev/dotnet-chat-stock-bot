using Chat.Web.Hubs;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Chat.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real <c>Chat.Web</c> application in memory, with two substitutions: the database is the
/// throwaway container's, and the message broker is MassTransit's in-memory test harness.
/// </summary>
/// <remarks>
/// <b>The bus must be substituted, not just the outbound port.</b> Since task 1.16 <c>Chat.Web</c>
/// registers a real receive endpoint (<c>stock-quote-responses</c>), so MassTransit opens a RabbitMQ
/// connection during startup: replacing only <c>IStockQuoteRequester</c> would leave
/// <see cref="WebApplicationFactory{TEntryPoint}"/> waiting on a broker that a reviewer's machine need
/// not have. <see cref="DependencyInjectionTestingExtensions.AddMassTransitTestHarness(IServiceCollection,Action{IBusRegistrationConfigurator})"/>
/// replaces the transport of the already-configured bus with the in-memory one and keeps every consumer
/// registration, so the topology under test is the shipped one — the real publisher adapters, the real
/// consumer, on the endpoint names <c>MessagingConstants</c> pins — with nothing to connect to.
/// </remarks>
/// <remarks>
/// <para>
/// The entry-point type parameter is <see cref="ChatHub"/> rather than <c>Program</c>: this project
/// references both hosts, and each has its own top-level <c>Program</c> in the global namespace, so naming
/// it would be ambiguous. <see cref="WebApplicationFactory{TEntryPoint}"/> only uses the type to find its
/// assembly, so any public type from <c>Chat.Web</c> identifies the host just as precisely.
/// </para>
/// </remarks>
public sealed class ChatApplicationFactory : WebApplicationFactory<ChatHub>
{
    /// <summary>
    /// How long a bus assertion (<c>harness.Published.Any&lt;T&gt;()</c>) waits before failing. Bounded so
    /// a wiring mistake fails in seconds instead of hanging the suite.
    /// </summary>
    public static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// What the harness itself is told, as opposed to what a single assertion waits for.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately far longer than any run.</b> The harness is started once per collection, so its
    /// inactivity timer is armed for the whole suite rather than for one test — and when it fires while a
    /// test is enumerating <c>Published</c>, MassTransit 8.5.10 deadlocks: measured with two stacks, the test
    /// thread holds <c>AsyncElementList</c>'s lock inside the enumerator's <c>finally</c> and blocks in
    /// <c>CancellationTokenSource.Registrations.WaitForCallbackToComplete</c>, while the timer thread runs
    /// <c>AsyncInactivityObserver.NoActivity</c> into that list's cancel callback and blocks on
    /// <c>Monitor.Enter</c> for the same lock. Neither side can proceed and the test hangs until xUnit's
    /// timeout kills it.
    /// <para>
    /// Making the timer unreachable is what closes that window. It costs no coverage, because every wait a
    /// test performs is bounded by <see cref="BusTimeout"/> through a token the test owns — see
    /// <c>ChatServerFixture.PublishedAsync</c>. Cancellation therefore only ever comes from us, at a moment
    /// no enumeration is in progress.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan HarnessTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The configuration key <c>AddPersistence</c> reads, in its environment-variable spelling.
    /// </summary>
    private const string ConnectionStringVariable = "ConnectionStrings__ChatDatabase";

    private readonly string? previousConnectionString;

    /// <summary>Creates a factory whose application talks to the given database.</summary>
    /// <param name="connectionString">Connection string of the throwaway SQL Server container.</param>
    /// <remarks>
    /// <b>Why an environment variable and not <c>ConfigureAppConfiguration</c>:</b> <c>AddPersistence</c>
    /// reads the connection string while <c>Program</c> is still registering services — and throws when it
    /// is blank, which the committed <c>appsettings.json</c> deliberately is — so the value has to be in
    /// place before the host builder exists, which rules out any test-side configuration callback. An
    /// environment variable is also the last configuration source <c>WebApplication.CreateBuilder</c> adds,
    /// so it wins over both <c>appsettings.json</c> and the developer's user secrets: the suite can never
    /// be pointed at the real <c>ChatDb</c> by whatever happens to be on the machine. It is the same route
    /// the README documents for deployment.
    /// </remarks>
    public ChatApplicationFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        previousConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        Environment.SetEnvironmentVariable(ConnectionStringVariable, connectionString);
    }

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // WebApplicationFactory already defaults to Development, but the suite depends on it rather than
        // tolerating it: outside Development AddChatIdentity pins the authentication cookie to HTTPS, and
        // an Always cookie would never come back over TestServer's plain HTTP, so every login would look
        // like a wrong password. This is the same configuration as the documented local run on port 5271.
        builder.UseEnvironment(Environments.Development);

        // The harness owns the bus lifecycle from here: it replaces MassTransit's hosted service, so
        // ChatServerFixture must call harness.Start() — building the host is not enough.
        builder.ConfigureTestServices(services =>
            services.AddMassTransitTestHarness(configurator =>
                configurator.SetTestTimeouts(HarnessTimeout, HarnessTimeout)));
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            Environment.SetEnvironmentVariable(ConnectionStringVariable, previousConnectionString);
        }
    }
}
