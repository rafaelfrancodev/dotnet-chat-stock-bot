namespace Chat.Domain.Messages;

/// <summary>Domain-wide limits for chat messages. No magic numbers anywhere else.</summary>
public static class MessageConstants
{
    /// <summary>The chat window shows only this many messages (challenge requirement).</summary>
    public const int LatestMessagesCount = 50;

    /// <summary>Upper bound enforced by the MessageContent value object.</summary>
    public const int MaxContentLength = 500;
}
