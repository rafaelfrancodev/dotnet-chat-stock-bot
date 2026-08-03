using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Infrastructure;

/// <summary>
/// Composition entry point for the Infrastructure layer. Split per concern so a host can opt in to
/// only what it needs: Chat.Web needs persistence + Identity + messaging, Chat.Bot needs messaging + Stooq.
/// </summary>
public static class DependencyInjection
{
    /// <summary>EF Core, Identity stores and repository implementations. Used by Chat.Web only.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration) => services;

    /// <summary>RabbitMQ connection, publishers and consumers. Used by both hosts.</summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration) => services;

    /// <summary>Typed Stooq HTTP client and CSV parsing. Used by Chat.Bot only.</summary>
    public static IServiceCollection AddStockQuotes(this IServiceCollection services, IConfiguration configuration) => services;
}
