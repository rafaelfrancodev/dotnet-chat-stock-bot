namespace Chat.IntegrationTests.Infrastructure;

/// <summary>
/// Puts every integration test in one collection, so the SQL Server container and the in-memory host are
/// started once and the tests run one after another.
/// </summary>
/// <remarks>
/// Serialising them is a feature, not a limitation: they share a database and a bus, and a deterministic
/// order is what lets the suite assert "no command row exists in the whole table" rather than only in the
/// room it happened to use.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class ChatServerCollection : ICollectionFixture<ChatServerFixture>
{
    /// <summary>Name the tests reference in <c>[Collection]</c>.</summary>
    public const string Name = "Chat server";
}
