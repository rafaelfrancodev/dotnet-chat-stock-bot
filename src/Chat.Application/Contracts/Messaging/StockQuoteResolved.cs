using System.Text.Json.Serialization;

namespace Chat.Application.Contracts.Messaging;

/// <summary>
/// Wire contract published by Chat.Bot once it has (or has failed to) resolve a quote, consumed by Chat.Web.
/// The bot owns the wording, so Chat.Web never has to re-implement the message format.
/// </summary>
/// <param name="RequestId">Echo of <see cref="StockQuoteRequested.RequestId"/>.</param>
/// <param name="ChatRoomId">Room to post into.</param>
/// <param name="StockCode">Echo of the requested code, for logging and tests.</param>
/// <param name="Outcome">Whether a quote was found, the symbol was unknown, or the lookup failed.</param>
/// <param name="Price">Closing price when <paramref name="Outcome"/> is <see cref="StockQuoteOutcome.Quoted"/>; otherwise null.</param>
/// <param name="Message">Ready-to-post text, e.g. "AAPL.US quote is $93.42 per share".</param>
/// <param name="ResolvedAtUtc">When the bot produced the answer.</param>
public sealed record StockQuoteResolved(
    Guid RequestId,
    Guid ChatRoomId,
    string StockCode,
    StockQuoteOutcome Outcome,
    decimal? Price,
    string Message,
    DateTimeOffset ResolvedAtUtc);

/// <summary>Why the bot answered the way it did.</summary>
/// <remarks>
/// Serialised as its name rather than its ordinal. Measured: MassTransit's default
/// <c>System.Text.Json</c> options write an enum as a number, which would let a member inserted into
/// this list re-interpret messages already queued while the two hosts are deployed apart. The converter
/// is declared on the type so it holds wherever the contract is serialised, not only on the bus.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<StockQuoteOutcome>))]
public enum StockQuoteOutcome
{
    /// <summary>Stooq returned a usable closing price.</summary>
    Quoted = 0,

    /// <summary>Stooq answered, but the symbol is unknown (N/D fields).</summary>
    SymbolNotFound = 1,

    /// <summary>Stooq was unreachable, timed out, or returned something we could not parse.</summary>
    LookupFailed = 2,
}
