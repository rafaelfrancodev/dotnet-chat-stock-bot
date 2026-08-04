namespace Chat.Application.Abstractions.Hosting;

/// <summary>
/// Marks a use case that <c>Chat.Bot</c> runs.
/// </summary>
/// <remarks>
/// A handler marked with this must not depend on persistence: the bot never calls
/// <c>AddPersistence()</c>, which is the structural guarantee that it stays decoupled from the database.
/// Registering a persistence-dependent handler here would fail at startup rather than silently give the
/// bot database access — which is the intended trade.
/// </remarks>
public interface IBotFeature;
