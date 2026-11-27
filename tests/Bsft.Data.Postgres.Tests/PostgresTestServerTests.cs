using Bsft.Tests.Shared;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Postgres.Tests;

/// <summary>A run without a server must report its PostgreSQL facts as skipped, and a run with one must run them.</summary>
public sealed class PostgresTestServerTests
{
    private static Func<string, string?> Environment(string? connectionString, string? dockerOptIn, string? continuousIntegration = null) =>
        variable => variable switch
        {
            PostgresTestServer.ConnectionStringVariable => connectionString,
            PostgresTestServer.DockerOptInVariable => dockerOptIn,
            PostgresTestServer.ContinuousIntegrationVariable => continuousIntegration,
            _ => null,
        };

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public void ContinuousIntegrationWithoutServer_RunsTheFactsAndFailsTheFixture(string continuousIntegration)
    {
        var environment = Environment(null, null, continuousIntegration);

        PostgresTestServer.SkipReasonWithoutDocker(environment).Should().BeNull();
        PostgresTestServer.SkipReason(environment).Should().BeNull();
        var require = () => PostgresTestServer.RequireServerOnContinuousIntegration(environment);
        require.Should().Throw<InvalidOperationException>().WithMessage($"*{PostgresTestServer.ConnectionStringVariable}*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void NoContinuousIntegrationWithoutServer_SkipsAndLeavesTheFixtureAlone(string? continuousIntegration)
    {
        var environment = Environment(null, null, continuousIntegration);

        PostgresTestServer.SkipReason(environment).Should().NotBeNull();
        var require = () => PostgresTestServer.RequireServerOnContinuousIntegration(environment);
        require.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void SkipReasonWithoutDocker_NoConnectionString_NamesTheVariable(string? connectionString) =>
        PostgresTestServer.SkipReasonWithoutDocker(Environment(connectionString, "1"))
            .Should().Contain(PostgresTestServer.ConnectionStringVariable);

    [Fact]
    public void SkipReasonWithoutDocker_ConnectionString_RunsTheFact() =>
        PostgresTestServer.SkipReasonWithoutDocker(Environment("Host=db", null)).Should().BeNull();

    [Fact]
    public void SkipReason_NeitherVariable_NamesBoth() =>
        PostgresTestServer.SkipReason(Environment(null, " "))
            .Should().Contain(PostgresTestServer.ConnectionStringVariable)
            .And.Contain(PostgresTestServer.DockerOptInVariable);

    [Theory]
    [InlineData("Host=db", null)]
    [InlineData(null, "1")]
    public void SkipReason_EitherVariable_RunsTheFact(string? connectionString, string? dockerOptIn) =>
        PostgresTestServer.SkipReason(Environment(connectionString, dockerOptIn)).Should().BeNull();
}
