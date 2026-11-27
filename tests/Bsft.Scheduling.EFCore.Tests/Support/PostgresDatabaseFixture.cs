using Bsft.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests.Support;

/// <summary>
/// Provides the PostgreSQL facts with a real server: the one the <c>BSFT_TEST_POSTGRES</c> connection
/// string names (CI starts one, see <c>.github/postgres-service.sh</c>). Without it every fact that
/// needs the server is reported as skipped by <c>[PostgresFact]</c>.
/// </summary>
/// <remarks>
/// No Testcontainers here, unlike the fixtures of the framework-tier suites: this package is a
/// Foundation package, whose NuGet audit covers the whole graph, and the container client's SSH.NET
/// dependency carries advisories (NU1903). Each database is created fresh and dropped on disposal.
/// </remarks>
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    private const string ConnectionStringEnvironmentVariable = "BSFT_TEST_POSTGRES";

    private readonly List<string> _createdDatabases = [];
    private string _adminConnectionString = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            PostgresTestServer.RequireServerOnContinuousIntegration(Environment.GetEnvironmentVariable);
            return;
        }

        try
        {
            await using var probe = new NpgsqlConnection(connectionString);
            await probe.OpenAsync();
        }
        catch (Exception exception) when (exception is NpgsqlException or ArgumentException or TimeoutException)
        {
            // The message names no connection string, which carries the password.
            throw new InvalidOperationException(
                $"{ConnectionStringEnvironmentVariable} names a server that did not accept a connection "
                + $"({exception.GetType().Name}). Start that server, or unset {ConnectionStringEnvironmentVariable} "
                + "to skip the PostgreSQL facts.",
                exception);
        }

        _adminConnectionString = connectionString;
    }

    /// <summary>Creates a fresh database, builds its schema, and returns options bound to it.</summary>
    /// <param name="purpose">A short marker included in the database name, for diagnosability.</param>
    /// <returns>Context options bound to the new database.</returns>
    public async Task<DbContextOptions<TestPostgresJobDbContext>> CreateDatabaseAsync(string purpose)
    {
        var name = $"scheduling_{purpose}_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(_adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\"";
            await create.ExecuteNonQueryAsync();
        }

        _createdDatabases.Add(name);

        var target = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = name };
        var options = new DbContextOptionsBuilder<TestPostgresJobDbContext>().UseNpgsql(target.ConnectionString).Options;

        // EnsureCreated rather than a migration: this framework ships none, and a host generates its
        // own against its own context.
        await using (var db = new TestPostgresJobDbContext(options))
            await db.Database.EnsureCreatedAsync();

        return options;
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
            await drop.ExecuteNonQueryAsync();
        }
    }
}
