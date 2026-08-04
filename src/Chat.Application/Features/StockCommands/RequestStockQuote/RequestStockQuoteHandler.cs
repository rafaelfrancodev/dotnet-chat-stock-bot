using Chat.Application.Abstractions.Messaging;
using Chat.Application.Abstractions.Stocks;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Messaging;
using Chat.Domain.Common;

namespace Chat.Application.Features.StockCommands.RequestStockQuote;

/// <summary>
/// Publishes a stock lookup for the bot to pick up, and nothing else.
/// </summary>
/// <remarks>
/// <b>This handler is the structural guarantee behind the challenge's hard constraint.</b> Its
/// constructor takes no <c>IMessageRepository</c>, no <c>IChatRoomRepository</c> and no
/// <c>IUnitOfWork</c>, so the code path a <c>/stock=</c> command travels cannot write a row — not by
/// mistake, not by a later edit, not at all. A unit test asserts the constructor's parameter list, which
/// turns the guarantee into something the build enforces rather than something a reviewer has to trust.
/// <para>
/// It does not re-check that the room exists either: <c>PostMessageHandler</c> already did, and giving
/// this handler a repository to check with would hand back exactly the capability the constraint denies.
/// </para>
/// </remarks>
/// <param name="stockQuotes">The outbound port to the broker. Chat.Web never calls Stooq itself.</param>
/// <param name="clock">Stamps the request; the bot and the logs need one agreed instant.</param>
internal sealed class RequestStockQuoteHandler(
    IStockQuoteRequester stockQuotes,
    IDateTimeProvider clock)
    : ICommandHandler<RequestStockQuoteCommand>
{
    public async Task<Result> Handle(RequestStockQuoteCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        StockQuoteRequested quoteRequest = new(
            // Version 7: time-ordered, so correlating a request with its answer in the logs is a sort.
            RequestId: Guid.CreateVersion7(),
            ChatRoomId: request.ChatRoomId.Value,
            StockCode: request.StockCode.Value,
            RequestedByUserId: request.RequestedByUserId,
            RequestedByDisplayName: request.RequestedByDisplayName,
            RequestedAtUtc: clock.UtcNow);

        await stockQuotes.RequestAsync(quoteRequest, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
