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

        builder.ConfigureTestServices(services =>
        {
            services.AddMassTransitTestHarness(configurator =>
                configurator.SetTestTimeouts(BusTimeout, BusTimeout));

            // Without this the host is "started" while the bus is still starting, because MassTransit's
            // hosted service does not wait for it by default. A test that published in that window blocked
            // until its own timeout — which is exactly the intermittent stall that only ever hit the three
            // bus-dependent tests (`SendMessage did not answer within 30 seconds`) while the plain-message,
            // history and authentication tests always passed. Waiting for the bus removes the race.
            services.Configure<MassTransitHostOptions>(options =>
            {
                options.WaitUntilStarted = true;
                options.StartTimeout = BusTimeout;
                options.StopTimeout = BusTimeout;
            });
        });
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
