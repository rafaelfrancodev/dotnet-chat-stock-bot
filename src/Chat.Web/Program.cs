using Chat.Application;
using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Realtime;
using Chat.Infrastructure;
using Chat.Infrastructure.HealthChecks;
using Chat.Infrastructure.Persistence;
using Chat.Web.Hubs;
using Chat.Web.Identity;
using Chat.Web.Realtime;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Composition root: the only place in Chat.Web that knows about every layer.
// IWebFeature scopes the handler scan to the use cases this host serves.
builder.Services.AddApplication<IWebFeature>();
builder.Services.AddSystemClock();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);

// Registration, login and logout come from Identity's default UI over the same ChatDbContext.
builder.Services.AddChatIdentity(requireSecureCookie: !builder.Environment.IsDevelopment());

// The realtime adapter lives here because IHubContext belongs to the host that owns the hub.
builder.Services.AddSignalR();
builder.Services.AddScoped<IChatNotifier, SignalRChatNotifier>();

// MassTransit contributes its own "masstransit-bus" check via AddMessaging; AddChatBroker is what
// turns a broker outage into an unready host. See RabbitMqHealthCheck for the measurement.
builder.Services.AddHealthChecks()
    .AddChatDatabase(builder.Configuration)
    .AddChatBroker(builder.Configuration);

builder.Services.AddRazorPages();

WebApplication app = builder.Build();

// Schema and the default room, before the first request. Idempotent, so this is equivalent to (and
// safe alongside) running `dotnet ef database update` by hand; it logs whichever it did.
await app.Services.InitializeChatDatabaseAsync(app.Lifetime.ApplicationStopping);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapChatHealthChecks();

// Mapped only now that authentication exists: the hub carries [Authorize], so an anonymous negotiate is
// rejected by the framework before any hub method runs.
app.MapHub<ChatHub>(ChatHub.Route);

app.Run();

/// <summary>Exposed so Chat.IntegrationTests can host the application with WebApplicationFactory.</summary>
public partial class Program;
