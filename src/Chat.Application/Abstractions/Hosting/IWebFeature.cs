namespace Chat.Application.Abstractions.Hosting;

/// <summary>
/// Marks a use case that <c>Chat.Web</c> runs. Handlers carrying this marker may depend on persistence
/// and on <c>IChatNotifier</c>, because the web host registers both.
/// </summary>
/// <remarks>
/// The two hosts share one Application assembly, so an unfiltered assembly scan would register every
/// handler in both processes — including handlers whose dependencies a host deliberately does not have.
/// Marking each handler with the host that runs it turns that into an explicit, checked decision:
/// <c>AddApplication&lt;IWebFeature&gt;()</c> registers exactly these, and
/// <see cref="IBotFeature"/> registers exactly the bot's.
/// </remarks>
public interface IWebFeature;
