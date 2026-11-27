using Bsft.Data.Contracts.Pagination;
using Bsft.Data.KeysetPaging;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.KeysetPaging;

/// <summary>
/// The SQL the keyset predicate becomes: the row-value comparison spelled out with OR and AND, the order on
/// the key then the id, and the position as parameters rather than literals.
/// </summary>
public sealed class KeysetQueryComposerSqliteTests : IDisposable
{
    private static readonly DateTime Position = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Unspecified);

    private readonly SqliteConnection _connection;

    private readonly MeterReadingContext _context;

    private readonly KeysetQueryComposer _composer = new();

    public KeysetQueryComposerSqliteTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new MeterReadingContext(new DbContextOptionsBuilder<MeterReadingContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void Compose_Ascending_TranslatesToTheKeysetPredicateAndOrder()
    {
        var order = new KeysetOrder<MeterReading, DateTime, long>("readings", r => r.RecordedAt, r => r.Id);

        var sql = Statement(_composer.Compose(_context.Readings, order, new KeysetPosition<DateTime, long>(Position, 424242)).ToQueryString());

        sql.Should().MatchRegex(@"WHERE ""r""\.""RecordedAt"" > @\w+ OR \(""r""\.""RecordedAt"" = @\w+ AND ""r""\.""Id"" > @\w+\)");
        sql.Should().MatchRegex(@"ORDER BY ""r""\.""RecordedAt"", ""r""\.""Id""$");
        sql.Should().NotContain("424242").And.NotContain("2026-11-12");
    }

    [Fact]
    public void Compose_Descending_ComparesDownwardAndOrdersDescending()
    {
        var order = new KeysetOrder<MeterReading, DateTime, long>("readings", r => r.RecordedAt, r => r.Id, SortDirection.Desc);

        var sql = Statement(_composer.Compose(_context.Readings, order, new KeysetPosition<DateTime, long>(Position, 1)).ToQueryString());

        sql.Should().MatchRegex(@"""r""\.""RecordedAt"" < @\w+ OR \(""r""\.""RecordedAt"" = @\w+ AND ""r""\.""Id"" < @\w+\)");
        sql.Should().MatchRegex(@"ORDER BY ""r""\.""RecordedAt"" DESC, ""r""\.""Id"" DESC$");
    }

    [Fact]
    public void Compose_StringKey_ComparesTheColumnDirectly()
    {
        var order = new KeysetOrder<MeterReading, string, Guid>("readings", r => r.Meter, r => r.Serial);

        var sql = Statement(_composer.Compose(_context.Readings, order, new KeysetPosition<string, Guid>("meter-x", Guid.Empty)).ToQueryString());

        sql.Should().MatchRegex(@"WHERE ""r""\.""Meter"" > @\w+ OR \(""r""\.""Meter"" = @\w+ AND ""r""\.""Serial"" > @\w+\)");
        sql.Should().NotContain("'meter-x'");
    }

    [Fact]
    public void Compose_FirstPage_OnlyOrders()
    {
        var order = new KeysetOrder<MeterReading, DateTime, long>("readings", r => r.RecordedAt, r => r.Id);

        var sql = Statement(_composer.Compose(_context.Readings, order, null).ToQueryString());

        sql.Should().NotContain("WHERE");
        sql.Should().EndWith(@"ORDER BY ""r"".""RecordedAt"", ""r"".""Id""");
    }

    /// <summary>The statement without the parameter declarations SQLite's query string starts with.</summary>
    private static string Statement(string sql)
    {
        var index = sql.IndexOf("SELECT", StringComparison.Ordinal);
        return (index < 0 ? sql : sql[index..]).ReplaceLineEndings(" ").Trim();
    }
}
