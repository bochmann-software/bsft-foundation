using Xunit;

namespace Bsft.Tests.Shared;

/// <summary>A fact that needs the server <see cref="PostgresTestServer.ConnectionStringVariable"/> names; reported as skipped without one.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    /// <summary>Sets the skip reason from the environment.</summary>
    public PostgresFactAttribute() =>
        Skip = PostgresTestServer.SkipReasonWithoutDocker(Environment.GetEnvironmentVariable);
}
