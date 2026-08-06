namespace Chat.IntegrationTests.Infrastructure;

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself, with a reason, when Docker is unreachable — so
/// <c>dotnet test</c> never fails on a reviewer's machine for an environmental reason.
/// </summary>
/// <remarks>
/// The decision is taken in the constructor because xUnit v2 evaluates <see cref="FactAttribute.Skip"/>
/// during discovery and has no runtime skip (<c>Assert.Skip</c> arrived in v3). Every integration test
/// carries this attribute instead of <c>[Fact]</c>; a test that forgets it would fail rather than skip.
/// The discoverer is inherited from <see cref="FactAttribute"/> — the test itself is an ordinary fact.
/// </remarks>
public sealed class DockerFactAttribute : FactAttribute
{
    /// <summary>
    /// Upper bound on one test. Every wait inside a test is already bounded, but a hub invocation is not —
    /// <c>HubConnection.InvokeAsync</c> has no timeout of its own — so this is the backstop that turns a
    /// wedged server into a failure a reviewer can read instead of a build that never ends.
    /// </summary>
    public const int TimeoutMilliseconds = 60_000;

    /// <summary>Creates the attribute, skipping the test when Docker cannot be reached.</summary>
    public DockerFactAttribute()
    {
        Skip = DockerEnvironment.SkipReason;
        Timeout = TimeoutMilliseconds;
    }
}
