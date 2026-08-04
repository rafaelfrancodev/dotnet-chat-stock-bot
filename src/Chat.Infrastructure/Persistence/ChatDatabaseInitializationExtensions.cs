using Microsoft.Extensions.DependencyInjection;

namespace Chat.Infrastructure.Persistence;

/// <summary>
/// Startup hook a host calls once, after building its service provider, to migrate and seed the
/// database. Kept out of <c>DependencyInjection</c> because it runs work rather than registering it.
/// </summary>
public static class ChatDatabaseInitializationExtensions
{
    /// <summary>
    /// Applies pending migrations and seeds the default room, inside a scope of its own so the
    /// <see cref="ChatDbContext"/> used at startup is disposed before the host serves a request.
    /// </summary>
    /// <param name="services">The built application service provider.</param>
    /// <param name="cancellationToken">Cancels the work, e.g. when startup is interrupted.</param>
    /// <exception cref="InvalidOperationException">
    /// The host did not call <c>AddPersistence</c>, so there is no database to initialise.
    /// </exception>
    public static async Task InitializeChatDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        ChatDbSeeder seeder = scope.ServiceProvider.GetRequiredService<ChatDbSeeder>();

        await seeder.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }
}
