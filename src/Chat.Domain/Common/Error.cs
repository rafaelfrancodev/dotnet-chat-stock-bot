namespace Chat.Domain.Common;

/// <summary>
/// An expected, non-exceptional failure. Carries a stable machine-readable <paramref name="Code"/>
/// (used by tests and by the presentation layer) and a human-readable <paramref name="Message"/>.
/// </summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>Input violated a rule the caller can fix.</summary>
    public static Error Validation(string code, string message) => new(code, message);

    /// <summary>The requested aggregate does not exist.</summary>
    public static Error NotFound(string code, string message) => new(code, message);

    /// <summary>A downstream dependency (broker, HTTP API) refused or failed the operation.</summary>
    public static Error Failure(string code, string message) => new(code, message);

    public override string ToString() => $"{Code}: {Message}";
}
