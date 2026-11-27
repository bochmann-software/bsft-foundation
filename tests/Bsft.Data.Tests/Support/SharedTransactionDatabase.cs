using Bsft.Data.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.Tests.Support;

/// <summary>
/// A SQLite file both module contexts share, so that every scope opens its own connection to the same
/// database, the way a host's request scopes do. SQLite refuses a command on a connection with a
/// pending transaction unless the command names it, which makes a context that did not enlist fail
/// loudly instead of writing outside the transaction.
/// </summary>
public sealed class SharedTransactionDatabase : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bsft-shared-{Guid.NewGuid():N}.db");

    public SharedTransactionDatabase()
    {
        ConnectionString = $"Data Source={_path};Pooling=False";

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE CatalogItems(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);" +
            "CREATE TABLE StockItems(Id INTEGER PRIMARY KEY, Sku TEXT NOT NULL UNIQUE);" +
            "CREATE TABLE TestEntities(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Age INTEGER NOT NULL, " +
            "Email TEXT NOT NULL, Price TEXT NOT NULL, TenantId TEXT);";
        command.ExecuteNonQuery();
    }

    public string ConnectionString { get; }

    /// <summary>A provider with the shared transaction and both contexts enlisted in it.</summary>
    /// <param name="configure">Further registrations.</param>
    /// <param name="sqliteOptions">The stock context's own SQLite options.</param>
    public ServiceProvider BuildProvider(
        Action<IServiceCollection>? configure = null,
        Action<SqliteDbContextOptionsBuilder>? sqliteOptions = null)
    {
        var services = new ServiceCollection();
        services.AddBsftDataSharedTransaction(_ => new SqliteConnection(ConnectionString));
        services.AddDbContext<CatalogContext>((sp, options) => options
            .UseBsftSharedTransaction(sp, (builder, connection) => builder.UseSqlite(connection)));
        services.AddDbContext<StockContext>((sp, options) => options
            .UseBsftSharedTransaction(sp, (builder, connection) => builder.UseSqlite(connection, sqliteOptions)));
        configure?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Reads one value of the committed state, on a connection of its own.</summary>
    public object? Scalar(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar();
    }

    /// <summary>Counts the committed rows of a table, read on a connection of its own.</summary>
    public long Count(string table)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";

        return (long)command.ExecuteScalar()!;
    }

    public void Dispose() => File.Delete(_path);
}
