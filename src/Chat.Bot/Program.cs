using Chat.Bot;
using Chat.Infrastructure.HealthChecks;

// The bot is a worker that also serves health probes. It is a web host only so its dependencies can
// be inspected the same way as Chat.Web's — it exposes no chat surface and never references Chat.Web.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Composition root: the bot is a separate process and never references Chat.Web. What it registers lives
// in AddBotServices so BotCompositionTests can assert the absence of persistence rather than trust a
// comment here.
builder.Services.AddBotServices(builder.Configuration);

WebApplication app = builder.Build();

app.MapChatHealthChecks();

app.Run();
