using Chat.Domain.Messages;

namespace Chat.Application.Contracts.Messages;

/// <summary>
/// A chat post as it crosses the boundary to a client: projected in SQL by the repository, returned by
/// the latest-messages query and broadcast over SignalR.
/// </summary>
/// <remarks>
/// Lives in <c>Contracts</c> rather than inside a feature folder because three ports share it
/// (<c>IMessageRepository</c>, <c>IChatNotifier</c> and the query handler); putting it under one
/// feature would make the other two depend on that feature.
/// <para>
/// It deliberately exposes no <c>UserId</c>. The chat window needs a display name, and shipping the
/// authentication id of every participant to every browser would leak identifiers for nothing.
/// </para>
/// </remarks>
/// <param name="Id">Identity of the post.</param>
/// <param name="AuthorDisplayName">Name to render as the post owner — "Bot" for stock quote answers.</param>
/// <param name="Content">The post text. Rendered with <c>textContent</c> in the browser, never as HTML.</param>
/// <param name="PostedAtUtc">Post time in UTC. The ordering key of the chat window.</param>
/// <param name="Origin">Whether a participant or the bot produced this post.</param>
public sealed record MessageDto(
    Guid Id,
    string AuthorDisplayName,
    string Content,
    DateTimeOffset PostedAtUtc,
    MessageOrigin Origin);
