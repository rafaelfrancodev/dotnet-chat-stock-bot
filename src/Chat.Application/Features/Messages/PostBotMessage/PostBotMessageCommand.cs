using Chat.Application.Abstractions.Messaging;
using Chat.Application.Contracts.Messaging;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.Messages;

namespace Chat.Application.Features.Messages.PostBotMessage;

/// <summary>
/// The bot's answer to a <c>/stock=</c> command, on its way into the room it was asked in. Dispatched by
/// <c>Chat.Web</c>'s response consumer for every <see cref="StockQuoteResolved"/> the bot publishes.
/// </summary>
/// <remarks>
/// <b>The command carries no author.</b> <see cref="Message.PostByBot"/> takes none either, so "the post
/// owner is the bot" — the challenge's wording — is a property of the domain rather than something a
/// caller supplies. The same reasoning as <c>PostMessageCommand</c>, from the other direction: there the
/// author must come from the caller's claims, here it must not come from the caller at all.
/// <para>
/// <b>The wire contract stops at the consumer.</b> This command is not a <see cref="StockQuoteResolved"/>
/// and does not contain one: a use case that took the transport's record would be versioned with the
/// broker payload and could not be dispatched by anything else. It carries only the four values posting
/// the answer actually needs.
/// </para>
/// </remarks>
/// <param name="ChatRoomId">Room the quote was requested in, echoed back by the bot.</param>
/// <param name="Text">
/// The answer, already worded by the bot (<c>StockQuoteAnswer</c>) and posted <b>verbatim</b>. Chat.Web
/// never re-formats or appends to it: the sentence the challenge grades exists in exactly one place.
/// </param>
/// <param name="RequestedByUserId">
/// Participant who typed the command. Used only to aim an outage alert at them — never as the post's
/// author, and never rendered to the room.
/// </param>
/// <param name="Outcome">
/// Why the bot answered this way. Read for the alert decision and for logging, never for rendering:
/// <see cref="StockQuoteOutcome.LookupFailed"/> means the provider is down and the participant should be
/// told so out of band, while <see cref="StockQuoteOutcome.SymbolNotFound"/> is a real answer from a
/// working service and stays an ordinary chat line.
/// </param>
public sealed record PostBotMessageCommand(
    ChatRoomId ChatRoomId,
    string Text,
    string RequestedByUserId,
    StockQuoteOutcome Outcome)
    : ICommand
{
    /// <summary>Expected failures of this command, with stable codes for tests and clients.</summary>
    /// <remarks>
    /// Declared on the command because the handler is <c>internal</c>. Failures raised by
    /// <see cref="MessageContent"/>, by <see cref="Message"/> and the shared
    /// <c>ChatRoomErrors.NotFound</c> are returned as-is and are not restated here.
    /// </remarks>
    public static class Errors
    {
        /// <summary>
        /// The answer was committed but the fan-out to the connected participants did not complete.
        /// </summary>
        /// <remarks>
        /// Deliberately an expected failure rather than an exception, because of what the caller does with
        /// each: the consumer acknowledges a failed <see cref="Result"/> and lets an exception be retried.
        /// The post already exists at this point, so a retry would insert the same answer a second time,
        /// while the participants who missed the broadcast get it back from the last-50 history on their
        /// next join or refresh. A duplicate row is permanent; a missed push is not.
        /// </remarks>
        public static readonly Error NotAnnounced = Error.Failure(
            "BotMessage.NotAnnounced",
            "The bot's answer was saved but could not be delivered to the connected participants.");
    }
}
