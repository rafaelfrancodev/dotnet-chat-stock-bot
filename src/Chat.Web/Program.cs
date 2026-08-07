using Chat.Application;
using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Realtime;
using Chat.Infrastructure;
using Chat.Infrastructure.HealthChecks;
using Chat.Infrastructure.Messaging;
using Chat.Infrastructure.Persistence;
using Chat.Web.Hubs;
using Chat.Web.Identity;
using Chat.Web.Messaging;
using Chat.Web.Realtime;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Composition root: the only place in Chat.Web that knows about every layer.
// IWebFeature scopes the handler scan to the use cases this host serves.
builder.Services.AddApplication<IWebFeature>();
builder.Services.AddSystemClock();
builder.Services.AddPersistence(builder.Configuration);
// The response consumer is what creates the stock-quote-responses queue and its binding: until it is
// registered the bot's answers are published to an exchange with no route and RabbitMQ discards them.
// The extension — never AddConsumer<T>() — pins the endpoint to MessagingConstants' name rather than one
// derived from the consumer's class name.
builder.Services.AddMessaging(
    builder.Configuration,
    configurator => configurator.AddStockQuoteResponseConsumer<StockQuoteResponseConsumer>());

// Registration, login and logout come from Identity's default UI over the same ChatDbContext.
//
// The cookie is Secure everywhere but Development, and that default is deliberately not relaxed by
// deploying: it is only overridable by saying so. The switch exists because a container published on a
// plain HTTP port is a real deployment — and there the failure is silent, not loud. The browser simply
// never returns a Secure cookie over http, so every login appears to do nothing at all. Set
// Security__RequireSecureCookie=false only when the host is genuinely served without TLS.
builder.Services.AddChatIdentity(
    requireSecureCookie: builder.Configuration.GetValue(
        "Security:RequireSecureCookie",
        !builder.Environment.IsDevelopment()));

// The realtime adapter lives here because IHubContext belongs to the host that owns the hub.
builder.Services.AddSignalR();
builder.Services.AddScoped<IChatNotifier, SignalRChatNotifier>();
builder.Services.AddScoped<IChatRoomNotifier, SignalRChatRoomNotifier>();

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
