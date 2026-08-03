using Chat.Application;
using Chat.Infrastructure;
using Chat.Infrastructure.HealthChecks;

// The bot is a worker that also serves health probes. It is a web host only so its dependencies can
// be inspected the same way as Chat.Web's — it exposes no chat surface and never references Chat.Web.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Composition root: the bot is a separate process and never references Chat.Web.
builder.Services.AddApplication();
builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddStockQuotes(builder.Configuration);

// No database probe: the bot has no persistence by design, which is what keeps it decoupled.
builder.Services.AddHealthChecks()
    .AddChatBroker(builder.Configuration)
    .AddStooq(builder.Configuration);

WebApplication app = builder.Build();

app.MapChatHealthChecks();

app.Run();
