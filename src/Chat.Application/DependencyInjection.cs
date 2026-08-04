using Chat.Application.Abstractions.Hosting;
using Chat.Application.Behaviors;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Application;

/// <summary>
/// Composition entry point for the Application layer. Both hosts call it, each naming the feature set
/// it runs.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the pipeline, the validators and the handlers marked with <typeparamref name="TFeature"/>.
    /// </summary>
    /// <typeparam name="TFeature">
    /// <see cref="IWebFeature"/> for <c>Chat.Web</c>, <see cref="IBotFeature"/> for <c>Chat.Bot</c>.
    /// </typeparam>
    /// <param name="services">Service collection to register into.</param>
    /// <remarks>
    /// The hosts share one Application assembly, so an unfiltered scan would register every handler in
    /// both processes. The bot would then be asked to construct handlers that need a repository it
    /// deliberately does not have, and would fail at startup. Filtering by marker keeps each process to
    /// the use cases it actually serves, and keeps the bot's lack of database access structural rather
    /// than accidental.
    /// <para>
    /// Validators and pipeline behaviors are registered for both hosts unconditionally: they depend on
    /// nothing outside the Application layer, and a validator without its handler is inert.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddApplication<TFeature>(this IServiceCollection services)
        where TFeature : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMediatR(configuration =>
        {
            configuration.TypeEvaluator = static candidate => typeof(TFeature).IsAssignableFrom(candidate);
            configuration.RegisterServicesFromAssembly(AssemblyReference.Assembly);

            // Order matters: logging wraps validation, so rejected requests are still logged once.
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(AssemblyReference.Assembly, includeInternalTypes: true);

        return services;
    }
}
