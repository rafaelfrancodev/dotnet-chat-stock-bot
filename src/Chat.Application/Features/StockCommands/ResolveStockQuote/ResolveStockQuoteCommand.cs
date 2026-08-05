using Chat.Application.Abstractions.Messaging;
using Chat.Application.Contracts.Messaging;
using Chat.Domain.ChatRooms;
using Chat.Domain.StockCommands;

namespace Chat.Application.Features.StockCommands.ResolveStockQuote;

/// <summary>
/// The bot's half of the round trip: look one ticker up and publish the answer the room will see.
/// Dispatched by <c>Chat.Bot</c>'s consumer for every <see cref="StockQuoteRequested"/> it receives.
/// </summary>
/// <remarks>
/// It carries a <see cref="StockCode"/>, not a <see cref="string"/>, for the same reason
/// <c>RequestStockQuoteCommand</c> does: the only way to obtain one is the value object's factory, so the
/// ticker that reaches the Stooq URL has already passed the allow-list. Mapping the wire string onto the
/// value object is therefore the consumer's job, at the boundary, not the handler's.
/// <para>
/// The correlation id and the room are echoes of the request rather than values the bot mints — the whole
/// point of the id is that both hops share it. The resolution instant is not on the command: the handler
/// reads the clock after the lookup, so the stamp is when the answer was produced.
/// </para>
/// </remarks>
/// <param name="RequestId">Correlation id echoed from <see cref="StockQuoteRequested.RequestId"/>.</param>
/// <param name="ChatRoomId">Room the answer must be posted into.</param>
/// <param name="StockCode">The validated ticker to look up.</param>
/// <param name="RequestedByUserId">
/// Participant who typed the command, echoed back so Chat.Web can aim an outage alert at them alone.
/// The bot never posts as this participant; the answer's author is always the bot.
/// </param>
public sealed record ResolveStockQuoteCommand(
    Guid RequestId,
    ChatRoomId ChatRoomId,
    StockCode StockCode,
    string RequestedByUserId)
    : ICommand;
