using Chat.Application.Contracts.Messaging;

namespace Chat.Application.Abstractions.Stocks;

/// <summary>
/// Sends the bot's answer back to the chat. Used by Chat.Bot once a lookup has resolved;
/// implemented in Chat.Infrastructure over the message broker.
/// </summary>
/// <remarks>
/// The bot owns the wording of the answer, so Chat.Web never re-formats a quote — it posts what
/// arrives. The reply travels back through the broker rather than a direct call, which is what the
/// challenge means by a decoupled bot.
/// </remarks>
public interface IStockQuoteResponder
{
    /// <summary>Publishes a resolved answer for Chat.Web to post as the bot.</summary>
    /// <param name="response">
    /// The outcome and the ready-to-post text. Sent for failures too, so a participant always gets an
    /// answer rather than silence.
    /// </param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    Task RespondAsync(StockQuoteResolved response, CancellationToken cancellationToken = default);
}
