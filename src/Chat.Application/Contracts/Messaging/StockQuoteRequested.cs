namespace Chat.Application.Contracts.Messaging;

/// <summary>
/// Wire contract published by Chat.Web when a participant posts <c>/stock=code</c>, consumed by Chat.Bot.
/// Treat this type as a published API: additive changes only.
/// </summary>
/// <param name="RequestId">Correlates the request with its response; also used for idempotency and logging.</param>
/// <param name="ChatRoomId">Room the answer must be posted back to.</param>
/// <param name="StockCode">Already normalised and validated by the <c>StockCode</c> value object.</param>
/// <param name="RequestedByUserId">Identity of the requesting participant, taken from server-side claims.</param>
/// <param name="RequestedByDisplayName">Display name, carried only so the bot can mention the requester if needed.</param>
/// <param name="RequestedAtUtc">When Chat.Web accepted the command.</param>
public sealed record StockQuoteRequested(
    Guid RequestId,
    Guid ChatRoomId,
    string StockCode,
    string RequestedByUserId,
    string RequestedByDisplayName,
    DateTimeOffset RequestedAtUtc);
