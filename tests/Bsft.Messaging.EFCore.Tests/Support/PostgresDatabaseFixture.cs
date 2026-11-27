using Bsft.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.Support;

/// <summary>
/// Provides Postgres-only suites with the real PostgreSQL server the <c>BSFT_TEST_POSTGRES</c> connection
/// string names (CI starts one, see <c>.github/postgres-service.sh</c>); without it the facts are reported as skipped by <c>[PostgresFact]</c>.
/// </summary>
/// <remarks>
/// A real server rather than SQLite, because what these suites exist to check is provider-specific:
/// whether tenant-keyed tables with (TenantId, Id) composite keys and their unique indexes correctly
/// translate and run on Npgsql, and whether the time-ordered IDs are stored and retrieved as expected
/// on the real database.
/// <para>
/// <b>This fixture creates a fresh schema with <see cref="DatabaseFacade.EnsureCreatedAsync"/>, not a
/// migration.</b> This framework ships no migrations of its own — a consuming host generates its own
/// against its own <c>DbContext</c>, per docs/migration-1.3.11.md — so there is no existing-table
/// <c>ALTER COLUMN</c> for a repo-local test to run here. These tests verify the resulting column
/// mapping and query behavior on a fresh table; they do not verify a live <c>ALTER COLUMN</c> against
/// pre-existing rows, which stays a per-host concern documented in docs/migration-1.3.11.md.
/// </para>
/// </remarks>
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    private const string ConnectionStringEnvironmentVariable = "BSFT_TEST_POSTGRES";

    /// <summary>The longest identifier PostgreSQL keeps whole (NAMEDATALEN - 1); the rest is cut off.</summary>
    private const int MaxIdentifierLength = 63;

    private string _adminConnectionString = string.Empty;
    private readonly List<string> _createdDatabases = [];

    /// <summary>The application services of the contexts: a tenant-keyed id takes its time field from their clock.</summary>
    private readonly ServiceProvider _services =
        new ServiceCollection().AddSingleton(TimeProvider.System).BuildServiceProvider();

    /// <inheritdoc />
    /// <remarks>
    /// Throws only when <see cref="ConnectionStringEnvironmentVariable"/> names a server that does not
    /// accept a connection: a run that names a server means to test against it, so that is a failure,
    /// not a skip. An unset variable is reported by <c>[PostgresFact]</c>, which skips the fact.
    /// </remarks>
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
    /// suite instead of skipping its facts; only an unset variable skips.
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
            // The message names the server but never the connection string, which carries the password.
            throw new InvalidOperationException(
                $"{ConnectionStringEnvironmentVariable} names {DescribeServer(connectionString)}, which did not "
                + $"accept a connection ({exception.GetType().Name}: {exception.Message}). Start that server, "
                + $"or unset {ConnectionStringEnvironmentVariable} to skip the PostgreSQL facts.",
                exception);
        }

        _adminConnectionString = connectionString;
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

    /// <summary>Creates a fresh database for the plain (not tenant-keyed) PostgreSQL context.</summary>
    /// <param name="purpose">A short marker included in the database name, for diagnosability.</param>
    /// <returns>Options bound to the new database, whose schema is created.</returns>
    public Task<DbContextOptions<TestPostgresDbContext>> CreateDatabaseAsync(string purpose) =>
        CreateAsync<TestPostgresDbContext>(purpose, options => new TestPostgresDbContext(options));

    /// <summary>Creates a fresh database for the tenant-keyed context.</summary>
    /// <param name="purpose">A short marker included in the database name, for diagnosability.</param>
    /// <returns>Options bound to the new database, whose schema is created.</returns>
    public Task<DbContextOptions<TestTenantKeyedDbContext>> CreateTenantKeyedDatabaseAsync(string purpose) =>
        CreateAsync<TestTenantKeyedDbContext>(purpose, options => new TestTenantKeyedDbContext(options));

    private async Task<DbContextOptions<TContext>> CreateAsync<TContext>(
        string purpose, Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        // PostgreSQL cuts an identifier at 63 bytes, which a test name as purpose overruns, so the
        // unique part comes first.
        var name = $"msg_{Guid.NewGuid():N}_{purpose}";
        if (name.Length > MaxIdentifierLength)
            name = name[..MaxIdentifierLength];

        await using (var admin = new NpgsqlConnection(_adminConnectionString))
        {
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }

        _createdDatabases.Add(name);

        var target = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = name };
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(target.ConnectionString)
            .UseApplicationServiceProvider(_services)
            .Options;

        await using (var db = create(options))
            await db.Database.EnsureCreatedAsync();

        return options;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();

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
