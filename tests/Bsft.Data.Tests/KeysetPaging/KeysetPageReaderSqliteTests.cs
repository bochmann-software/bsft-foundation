using Bsft.Data.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Tests.KeysetPaging;

/// <summary>Keyset paging translated to SQL, on an in-memory SQLite database.</summary>
public sealed class KeysetPageReaderSqliteTests : KeysetPageReaderProviderTests
{
    private SqliteConnection? _connection;

    protected override MeterReadingContext CreateContext()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var context = new MeterReadingContext(new DbContextOptionsBuilder<MeterReadingContext>().UseSqlite(_connection).Options);
        context.Database.EnsureCreated();
        return context;
    }

    protected override void DisposeProvider() => _connection?.Dispose();
}
