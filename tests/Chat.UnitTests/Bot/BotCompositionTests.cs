using Chat.Application.Abstractions.Stocks;
using Chat.Bot;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.UnitTests.Bot;

/// <summary>
/// The challenge's decoupling requirement, asserted against the bot's composition root rather than
/// described in a comment.
/// </summary>
/// <remarks>
/// "The bot never touches the database" was previously guaranteed only by a comment in
/// <c>Program.cs</c> — a top-level statement file no test can call, so an <c>AddPersistence</c> added
/// there would compile, run and break nothing. The other tests do not close the gap either: they inspect
/// <i>handler constructors</i>, so a persistence service nothing happens to inject would go unnoticed.
/// <para>
/// These assertions are about registrations, not resolution, so no broker or database is contacted.
/// </para>
/// </remarks>
public sealed class BotCompositionTests
{
    /// <summary>Namespaces and types that would mean this process can reach the chat database.</summary>
    private static readonly string[] ForbiddenNamespaces =
    [
        "Chat.Infrastructure.Persistence",
        "Chat.Application.Abstractions.Persistence",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.SqlClient",
        "Microsoft.AspNetCore.Identity",
    ];

    [Fact]
    public void AddBotServices_RegistersNothingThatCanReachTheDatabase()
    {
        IServiceCollection services = Composed();

        IEnumerable<string> persistence = services
            .SelectMany(descriptor => new[] { descriptor.ServiceType, descriptor.ImplementationType })
            .Select(type => type?.FullName)
            .Where(name => name is not null && ForbiddenNamespaces.Any(
                forbidden => name.StartsWith(forbidden, StringComparison.Ordinal)))
            .Select(name => name!)
            .Distinct();

        persistence.Should().BeEmpty(
            "the bot must reach the chat only through the broker — a persistence registration here is "
            + "how AddPersistence creeps into this host and the decoupling requirement quietly breaks");
    }

    /// <summary>
    /// The negative assertion above only means something if the composition is real, so this proves the
    /// same call registers what the bot does need. A misspelled or reordered registration would otherwise
    /// make <c>RegistersNothingThatCanReachTheDatabase</c> pass by registering nothing at all.
    /// </summary>
    [Fact]
    public void AddBotServices_RegistersTheQuoteProviderAndTheBusConsumer()
    {
        IServiceCollection services = Composed();

        services.Should().Contain(
            descriptor => descriptor.ServiceType == typeof(IStockQuoteProvider),
            "the bot's whole job is looking a quote up");
        services.Should().Contain(
            descriptor => descriptor.ImplementationType == typeof(StockQuoteRequestConsumer),
            "and answering requests that arrive over the broker");
        services.Should().Contain(
            descriptor => descriptor.ServiceType == typeof(IBusControl),
            "MassTransit's own hosted service is this host's worker");
    }

    [Fact]
    public void AddBotServices_NullConfiguration_Throws()
    {
        ServiceCollection services = [];

        services.Invoking(collection => collection.AddBotServices(null!))
            .Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// The bot's registrations, from an explicit empty configuration so a developer's own user-secrets
    /// cannot change what these tests see.
    /// </summary>
    private static IServiceCollection Composed()
    {
        ServiceCollection services = [];
        services.AddLogging();
        services.AddBotServices(new ConfigurationBuilder().Build());

        return services;
    }
}
