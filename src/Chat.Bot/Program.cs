using Chat.Application;
using Chat.Infrastructure;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Composition root: the bot is a separate process and never references Chat.Web.
builder.Services.AddApplication();
builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddStockQuotes(builder.Configuration);

IHost host = builder.Build();
host.Run();
