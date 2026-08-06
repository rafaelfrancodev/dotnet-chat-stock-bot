using DotNet.Testcontainers.Configurations;

namespace Chat.IntegrationTests.Infrastructure;

/// <summary>
/// Answers one question, once per process: can this machine run the throwaway SQL Server container the
/// integration suite needs?
/// </summary>
/// <remarks>
/// The answer has to be available at <b>test discovery</b> time, because xUnit v2 can only skip a test
/// from <see cref="FactAttribute.Skip"/> — there is no runtime skip. That is what
/// <see cref="DockerFactAttribute"/> uses, and why the probe below is synchronous and cheap.
/// <para>
/// The probe is Testcontainers' own endpoint resolution rather than a hand-rolled socket check:
/// <see cref="TestcontainersSettings.OS"/> walks the same providers (<c>DOCKER_HOST</c>, the Docker
/// context, the named pipe, the Unix socket, Docker Desktop) that starting a container would walk, and
/// leaves <see cref="IOperatingSystem.DockerEndpointAuthConfig"/> null when none of them answers. Asking
/// the library means the suite cannot disagree with itself about whether Docker is reachable.
/// </para>
/// </remarks>
internal static class DockerEnvironment
{
    /// <summary>
    /// Set this to <c>1</c> (or <c>true</c>) to skip the integration suite without touching Docker — for a
    /// build agent that has no daemon, or for a quick unit-tests-only run.
    /// </summary>
    public const string SkipVariableName = "CHAT_TESTS_SKIP_DOCKER";

    private static readonly Lazy<string?> LazySkipReason =
        new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Why the integration tests cannot run here, or <see langword="null"/> when they can. Used verbatim
    /// as the xUnit skip reason, so a reviewer reading the test output learns what to fix.
    /// </summary>
    public static string? SkipReason => LazySkipReason.Value;

    /// <summary><see langword="true"/> when a container can be started on this machine.</summary>
    public static bool IsAvailable => SkipReason is null;

    private static string? Probe()
    {
        string? optOut = Environment.GetEnvironmentVariable(SkipVariableName);

        if (IsTruthy(optOut))
        {
            return $"Skipped on request: the environment variable {SkipVariableName} is set to \"{optOut}\". "
                + "Unset it to run the integration tests.";
        }

        try
        {
            if (TestcontainersSettings.OS.DockerEndpointAuthConfig is null)
            {
                return NotReachable("no Docker endpoint answered");
            }
        }
        catch (Exception exception)
        {
            // Resolution itself failing is the same outcome for us — there is no daemon to talk to — and
            // an environmental problem must never turn into a red test suite. The reason carries the
            // detail so it is still diagnosable.
            return NotReachable(exception.Message);
        }

        return null;
    }

    private static string NotReachable(string detail) =>
        $"Docker is not available ({detail}), so the throwaway SQL Server container cannot be started. "
        + "Start Docker Desktop and re-run, or set "
        + $"{SkipVariableName}=1 to skip these tests deliberately. The unit test suite needs no Docker.";

    private static bool IsTruthy(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Equals("0", StringComparison.OrdinalIgnoreCase)
        && !value.Equals("false", StringComparison.OrdinalIgnoreCase);
}
