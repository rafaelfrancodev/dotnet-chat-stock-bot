using Chat.Application;
using Chat.Application.Abstractions.Hosting;
using Chat.Bot;
using Chat.Infrastructure;
using Chat.Infrastructure.HealthChecks;
using Chat.Infrastructure.Messaging;

// The bot is a worker that also serves health probes. It is a web host only so its dependencies can
// be inspected the same way as Chat.Web's — it exposes no chat surface and never references Chat.Web.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Composition root: the bot is a separate process and never references Chat.Web.
// IBotFeature scopes the handler scan to the bot's own use cases, so the shared Application assembly
// never asks this host to construct a handler that needs the database it deliberately does not have.
builder.Services.AddApplication<IBotFeature>();
builder.Services.AddSystemClock();

// The bus is this host's worker: MassTransit's hosted service owns the connection and hands every
// StockQuoteRequested to the consumer below, so no BackgroundService of our own is needed. The
// extension — never AddConsumer<T>() — is what pins the endpoint to MessagingConstants'
// "stock-quote-requests" instead of a name derived from the consumer's class name.
builder.Services.AddMessaging(
    builder.Configuration,
    configurator => configurator.AddStockQuoteRequestConsumer<StockQuoteRequestConsumer>());

builder.Services.AddStockQuotes(builder.Configuration);

// No database probe: the bot has no persistence by design, which is what keeps it decoupled.
builder.Services.AddHealthChecks()
    .AddChatBroker(builder.Configuration)
    .AddStooq(builder.Configuration);

WebApplication app = builder.Build();

app.MapChatHealthChecks();

app.Run();
