namespace Chat.Infrastructure.Stocks;

/// <summary>
/// The outbound HTTP budget every quote provider shares: how many attempts one lookup may spend, how long
/// to wait between them, and how much of a response may be buffered.
/// </summary>
/// <remarks>
/// Provider-neutral on purpose. These numbers describe what the <i>bot</i> can afford per <c>/stock=</c>
/// command — a participant is waiting on the answer — not anything about a particular vendor, so they must
/// not live on one adapter and be read by another's registration. Only the total budget varies per
/// provider, and that comes from each provider's own <c>TimeoutSeconds</c>.
/// </remarks>
internal static class StockQuoteHttpDefaults
{
    /// <summary>
    /// Attempts spent on one lookup, initial call included. Also divides the request budget into the
    /// per-attempt timeout, so a slow service cannot consume the whole budget on its first try.
    /// </summary>
    internal const int MaxAttemptsPerLookup = 3;

    /// <summary>Base backoff between attempts; the standard handler grows it exponentially with jitter.</summary>
    internal const int RetryDelayMilliseconds = 250;

    /// <summary>
    /// Ceiling on the buffered response. A quote is a few hundred bytes at most; this only stops a
    /// redirected or hostile endpoint from streaming an unbounded body into the bot's memory.
    /// </summary>
    internal const int MaxResponseBytes = 64 * 1024;
}
