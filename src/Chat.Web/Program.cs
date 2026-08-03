using Chat.Application;
using Chat.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Composition root: the only place in Chat.Web that knows about every layer.
builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);

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

app.Run();

/// <summary>Exposed so Chat.IntegrationTests can host the application with WebApplicationFactory.</summary>
public partial class Program;
