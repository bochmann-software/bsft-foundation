using Xunit;

namespace Bsft.Tests.Shared;

/// <summary>A theory that needs the server <see cref="PostgresTestServer.ConnectionStringVariable"/> names; reported as skipped without one.</summary>
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    /// <summary>Sets the skip reason from the environment.</summary>
    public PostgresTheoryAttribute() =>
        Skip = PostgresTestServer.SkipReasonWithoutDocker(Environment.GetEnvironmentVariable);
}
