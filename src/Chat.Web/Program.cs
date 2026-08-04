using Chat.Application;
using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Realtime;
using Chat.Infrastructure;
using Chat.Infrastructure.HealthChecks;
using Chat.Web.Realtime;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Composition root: the only place in Chat.Web that knows about every layer.
// IWebFeature scopes the handler scan to the use cases this host serves.
builder.Services.AddApplication<IWebFeature>();
builder.Services.AddSystemClock();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);

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

app.Run();

/// <summary>Exposed so Chat.IntegrationTests can host the application with WebApplicationFactory.</summary>
public partial class Program;
