using Chat.Application.Abstractions.Messaging;
using Chat.Domain.ChatRooms;
using Chat.Domain.StockCommands;

namespace Chat.Application.Features.StockCommands.RequestStockQuote;

/// <summary>
/// Hands a recognised <c>/stock=code</c> command to the bot. Dispatched by <c>PostMessageHandler</c>
/// after the parser has classified the line and after the room has been confirmed to exist.
/// </summary>
/// <remarks>
/// It carries a <see cref="StockCode"/>, not a <see cref="string"/>: the only way to obtain one is the
/// value object's factory, so a ticker that reaches the broker — and from there the Stooq URL — has
/// already passed the allow-list. The command cannot express an unvalidated symbol.
/// <para>
/// The correlation id and the timestamp are deliberately absent. The handler mints them from
/// <c>Guid.CreateVersion7()</c> and the application clock, so no caller can replay a request id or
/// backdate a request.
/// </para>
/// </remarks>
/// <param name="ChatRoomId">Room the bot must answer into.</param>
/// <param name="StockCode">The validated ticker.</param>
/// <param name="RequestedByUserId">
/// Authentication id of the requester, taken from the caller's claims — never from a client payload.
/// </param>
/// <param name="RequestedByDisplayName">
/// Display name of the requester, taken from the caller's claims — never from a client payload.
/// </param>
public sealed record RequestStockQuoteCommand(
    ChatRoomId ChatRoomId,
    StockCode StockCode,
    string RequestedByUserId,
    string RequestedByDisplayName)
    : ICommand;
