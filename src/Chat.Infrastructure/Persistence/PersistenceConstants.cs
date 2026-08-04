namespace Chat.Infrastructure.Persistence;

/// <summary>
/// Conventions shared by the mapping, the composition root and the health check, so a column width or
/// a configuration key is never spelled out twice.
/// </summary>
internal static class PersistenceConstants
{
    /// <summary>Name of the connection string entry every host reads the database from.</summary>
    public const string ConnectionStringName = "ChatDatabase";

    /// <summary>Transient-fault retries. Covers the ~30 s SQL Server container start on a cold machine.</summary>
    public const int MaxRetryCount = 5;

    /// <summary>Upper bound of the exponential back-off between those retries.</summary>
    public const int MaxRetryDelaySeconds = 10;

    /// <summary>
    /// Width of any column holding an authentication user id. Matches Identity's own
    /// <c>AspNetUsers.Id</c> (<c>nvarchar(450)</c>, the widest key SQL Server can index), so a value
    /// copied out of Identity always fits.
    /// </summary>
    public const int UserIdMaxLength = 450;

    /// <summary>
    /// Width of any column holding a display name. Matches Identity's <c>UserName</c>
    /// (<c>nvarchar(256)</c>), which is where display names come from.
    /// </summary>
    public const int DisplayNameMaxLength = 256;
}
