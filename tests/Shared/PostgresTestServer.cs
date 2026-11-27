namespace Bsft.Tests.Shared;

/// <summary>
/// Decides, from the environment, whether this run has a PostgreSQL server for the facts that need one. A fact that
/// returned early without a server counted as passed, so a run without PostgreSQL looked as if it had proved them.
/// </summary>
public static class PostgresTestServer
{
    /// <summary>Holds the connection string of an existing server; CI sets it, see <c>.github/postgres-service.sh</c>.</summary>
    public const string ConnectionStringVariable = "BSFT_TEST_POSTGRES";

    /// <summary>Opts into a Testcontainers-started server, in the test projects whose fixture can start one.</summary>
    public const string DockerOptInVariable = "BSFT_TEST_POSTGRES_DOCKER";

    /// <summary>Set to <c>true</c> by GitHub Actions; a job that lost its server must fail, not go green on skips.</summary>
    public const string ContinuousIntegrationVariable = "CI";

    /// <summary>Why a fact needing an existing server does not run, or <see langword="null"/> when it does.</summary>
    /// <param name="read">Reads an environment variable.</param>
    /// <returns>The skip reason.</returns>
    public static string? SkipReasonWithoutDocker(Func<string, string?> read) =>
        IsContinuousIntegration(read) || IsSet(read, ConnectionStringVariable)
            ? null
            : $"No PostgreSQL server for this fact: {ConnectionStringVariable} is not set.";

    /// <summary>Why a fact able to use a started server does not run, or <see langword="null"/> when it does.</summary>
    /// <param name="read">Reads an environment variable.</param>
    /// <returns>The skip reason.</returns>
    public static string? SkipReason(Func<string, string?> read) =>
        IsContinuousIntegration(read) || IsSet(read, ConnectionStringVariable) || IsSet(read, DockerOptInVariable)
            ? null
            : $"No PostgreSQL server for this fact: neither {ConnectionStringVariable} nor {DockerOptInVariable} is set.";

    /// <summary>For a fixture that found no server: fails on a CI run, whose facts did not skip; elsewhere does nothing.</summary>
    /// <param name="read">Reads an environment variable.</param>
    /// <exception cref="InvalidOperationException">Thrown on a CI run.</exception>
    public static void RequireServerOnContinuousIntegration(Func<string, string?> read)
    {
        if (IsContinuousIntegration(read))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringVariable} is not set. A CI run must reach the PostgreSQL server; start one and set {ConnectionStringVariable}.");
        }
    }

    private static bool IsContinuousIntegration(Func<string, string?> read) =>
        string.Equals(read(ContinuousIntegrationVariable), "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsSet(Func<string, string?> read, string variable) =>
        !string.IsNullOrWhiteSpace(read(variable));
}
