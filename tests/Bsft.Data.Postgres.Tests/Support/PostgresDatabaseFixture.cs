using Bsft.Tests.Shared;
using Npgsql;
using Xunit;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Provides the PostgreSQL-only shared-transaction suite with a real server, named by the
/// <c>BSFT_TEST_POSTGRES</c> connection string.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as the fixtures of <c>Bsft.Jobs.EFCore.Tests</c> and <c>Bsft.Auditing.EFCore.Tests</c>,
/// without their Testcontainers fallback: this is a Foundation test project, whose package audit covers
/// transitive packages, and Testcontainers brings a vulnerable SSH.NET along. Locally an unset variable
/// is reported by <c>[PostgresFact]</c>, which skips the fact. A server the variable names but that does not accept a connection fails the
/// fixture instead: a run that names a server means to test against it. On a CI run (<c>CI=true</c>,
/// which GitHub Actions sets) an unset variable fails the fixture as well, so the suite can never pass
/// there by returning early; the workflows that run it start a PostgreSQL service and set the variable.
/// </para>
/// <para>
/// What runs here cannot run anywhere else: a serialization failure, an advisory transaction lock and
/// two schemas of one database are PostgreSQL behaviour. Everything the shared transaction is built out
/// of is covered on SQLite in <c>Bsft.Data.Tests</c>, so a run without a server loses this behavioural
/// proof and no coverage.
/// </para>
/// </remarks>
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    private const string ConnectionStringEnvironmentVariable = "BSFT_TEST_POSTGRES";

    private readonly List<string> _createdDatabases = [];
    private string _adminConnectionString = string.Empty;

    /// <summary>Whether a PostgreSQL server is actually available for this run.</summary>
    public bool IsAvailable { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            await UseConfiguredServerAsync(connectionString);
            return;
        }

        PostgresTestServer.RequireServerOnContinuousIntegration(Environment.GetEnvironmentVariable);
    }

    /// <summary>
    /// Connects once to the server <see cref="ConnectionStringEnvironmentVariable"/> names. A run that
    /// names a server means to test against it, so one that does not accept the connection fails the
    /// suite instead of skipping its facts.
    /// </summary>
    /// <param name="connectionString">The connection string the variable holds.</param>
    /// <returns>A task that completes once the server accepted the connection.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the server does not accept the connection.</exception>
    private async Task UseConfiguredServerAsync(string connectionString)
    {
        try
        {
            await using var probe = new NpgsqlConnection(connectionString);
            await probe.OpenAsync();
        }
        catch (Exception exception) when (exception is NpgsqlException or ArgumentException or TimeoutException)
        {
            // The message names the server but never the connection string, which may carry a password.
            throw new InvalidOperationException(
                $"{ConnectionStringEnvironmentVariable} names {DescribeServer(connectionString)}, which did not "
                + $"accept a connection ({exception.GetType().Name}: {exception.Message}). Start that server, "
                + $"or unset {ConnectionStringEnvironmentVariable} to skip the PostgreSQL facts.",
                exception);
        }

        _adminConnectionString = connectionString;
        IsAvailable = true;
    }

    /// <summary>Names the server a connection string points at, leaving its password out.</summary>
    /// <param name="connectionString">The connection string to describe.</param>
    /// <returns>The host, port and user, or a note that the value does not parse.</returns>
    private static string DescribeServer(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return $"the server at {builder.Host}:{builder.Port} as user {builder.Username}";
        }
        catch (ArgumentException)
        {
            return "a value that is not a valid connection string";
        }
    }

    /// <summary>
    /// Creates a fresh database with a <c>catalog</c> and an <c>inventory</c> schema, one per module
    /// context, and returns its connection string.
    /// </summary>
    /// <param name="purpose">A short marker included in the database name, for diagnosability.</param>
    /// <returns>The connection string of the new database.</returns>
    public async Task<string> CreateDatabaseAsync(string purpose)
    {
        var name = $"data_{purpose}_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(_adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\"";
            await create.ExecuteNonQueryAsync();
        }

        _createdDatabases.Add(name);

        var target = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = name }.ConnectionString;

        await using var connection = new NpgsqlConnection(target);
        await connection.OpenAsync();
        await using var schema = connection.CreateCommand();
        schema.CommandText =
            "CREATE SCHEMA catalog; CREATE SCHEMA inventory;" +
            "CREATE TABLE catalog.items (id serial PRIMARY KEY, name text NOT NULL);" +
            "CREATE TABLE inventory.stock (id integer PRIMARY KEY, sku text NOT NULL UNIQUE, quantity integer NOT NULL);" +
            "INSERT INTO inventory.stock (id, sku, quantity) VALUES (1, 'B-1', 0);";
        await schema.ExecuteNonQueryAsync();

        return target;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        foreach (var name in _createdDatabases)
        {
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)";
            // Dropping waits for a checkpoint, which under a whole-solution test run against the same
            // server can take longer than the 30 s default.
            drop.CommandTimeout = 300;
            await drop.ExecuteNonQueryAsync();
        }
    }
}
