namespace Chat.Domain.Messages;

/// <summary>
/// Who produced a persisted post. Explicit values because this is stored in the database.
/// </summary>
public enum MessageOrigin
{
    /// <summary>Written by a registered participant.</summary>
    Participant = 1,

    /// <summary>Written by the stock bot in answer to a <c>/stock=</c> command.</summary>
    Bot = 2,
}
