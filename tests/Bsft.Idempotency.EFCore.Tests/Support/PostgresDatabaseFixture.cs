using Bsft.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Bsft.Idempotency.EFCore.Tests.Support;

/// <summary>
/// A fresh PostgreSQL database on the server the <c>BSFT_TEST_POSTGRES</c> connection string names
/// (CI starts one, see <c>.github/postgres-service.sh</c>); without it every fact that touches the database
/// is reported as skipped by <c>[PostgresFact]</c>.
/// </summary>
/// <remarks>
/// A real server rather than SQLite, because what these suites check is provider-specific: the
/// (TenantId, Id) key and the unique indexes led by TenantId as Npgsql creates and enforces them.
/// Without a server the contexts still carry the tenant-keyed model, so the model-only facts run
/// everywhere; they get a connection string without credentials that is never opened.
/// </remarks>
public sealed class PostgresDatabaseFixture : IDisposable
{
    private const string ConnectionStringEnvironmentVariable = "BSFT_TEST_POSTGRES";

    /// <summary>A connection string for building the model only; nothing opens it.</summary>
    private const string ModelOnlyConnectionString = "Host=localhost";

    private readonly string? _adminConnectionString;
    private readonly string _connectionString = ModelOnlyConnectionString;
    private readonly string _databaseName = $"bsft_idem_{Guid.NewGuid():n}";

    /// <summary>The application services of the contexts: a tenant-keyed id takes its time field from their clock.</summary>
    private readonly ServiceProvider _services =
        new ServiceCollection().AddSingleton(TimeProvider.System).BuildServiceProvider();

    /// <summary>
    /// Creates the database when <c>BSFT_TEST_POSTGRES</c> is set. A run that names a server means to
    /// test against it, so a server that does not accept the connection fails instead of skipping.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the named server does not accept the connection.</exception>
    public PostgresDatabaseFixture()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            PostgresTestServer.RequireServerOnContinuousIntegration(Environment.GetEnvironmentVariable);
            return;
        }

        try
        {
            using var probe = new NpgsqlConnection(connectionString);
            probe.Open();
        }
        catch (Exception exception) when (exception is NpgsqlException or ArgumentException or TimeoutException)
        {
            // The message names the server but never the connection string, which carries the password.
            throw new InvalidOperationException(
                $"{ConnectionStringEnvironmentVariable} names a server that did not accept a connection "
                + $"({exception.GetType().Name}). Start that server, or unset {ConnectionStringEnvironmentVariable} "
                + "to skip the PostgreSQL facts.",
                exception);
        }

        _adminConnectionString = connectionString;
        _connectionString = new NpgsqlConnectionStringBuilder(connectionString) { Database = _databaseName }.ConnectionString;

        using var admin = new NpgsqlConnection(_adminConnectionString);
        admin.Open();
        using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
            create.ExecuteNonQuery();
        }

        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    /// <summary>A context with its own connection to the fixture's database (model only without a server).</summary>
    public TenantKeyedTestIdempotencyDbContext NewContext() => new(Options());

    /// <summary>
    /// A context over the same database, on its own connection, with interceptors attached for forcing
    /// save failures or committing a competing write mid-save.
    /// </summary>
    public TenantKeyedTestIdempotencyDbContext NewContextWith(params IInterceptor[] interceptors) =>
        new(Options(interceptors));

    private DbContextOptions<TenantKeyedTestIdempotencyDbContext> Options(params IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<TenantKeyedTestIdempotencyDbContext>()
            .UseNpgsql(_connectionString)
            .UseApplicationServiceProvider(_services)
            .AddInterceptors(interceptors)
            .Options;

    /// <inheritdoc />
    public void Dispose()
    {
        _services.Dispose();

        if (_adminConnectionString is null)
            return;

        NpgsqlConnection.ClearAllPools();
        using var admin = new NpgsqlConnection(_adminConnectionString);
        admin.Open();
        using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        drop.ExecuteNonQuery();
    }
}
