using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

/// <summary>
/// Proves the filter compiler emits its value as a query parameter rather than a SQL literal (so the
/// plan cache and any compiled-query cache are reused across distinct values), and that the opt-in
/// collation mode leaves the column bare so an index on it stays usable.
/// </summary>
public sealed class FilterCompilerParameterizationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContext _context;
    private readonly QueryFieldMap<TestEntity> _fieldMap;

    public FilterCompilerParameterizationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options;
        _context = new TestDbContext(options);
        _context.Database.EnsureCreated();

        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        new TestQueryConfiguration().Configure(builder);
        _fieldMap = builder.BuildFieldMap();
    }

    private static JsonElement JsonValue(object value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement JsonNull() => JsonSerializer.SerializeToElement<string?>(null);

    private static string Statement(string sql)
    {
        var index = sql.IndexOf("SELECT", StringComparison.Ordinal);
        return index < 0 ? sql : sql[index..];
    }

    [Fact]
    public void Compile_TwoDistinctValues_ProduceSameParameterizedSqlShape()
    {
        // Plan-cache reuse: two different filter values must yield the identical parameterized SQL
        // statement (only the bound parameter value differs), never two literal-laden SQL strings.
        var alice = new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonValue("Alice") };
        var bob = new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonValue("Bob") };

        var aliceSql = Statement(_context.TestEntities.Where(FilterCompiler.Compile<TestEntity>(alice, _fieldMap)).ToQueryString());
        var bobSql = Statement(_context.TestEntities.Where(FilterCompiler.Compile<TestEntity>(bob, _fieldMap)).ToQueryString());

        aliceSql.Should().Be(bobSql);
        aliceSql.Should().Contain("@");
        // The value is a bound parameter, not a literal baked into the statement text.
        aliceSql.Should().NotContainEquivalentOf("Alice");
        aliceSql.Should().NotContainEquivalentOf("Bob");
    }

    [Fact]
    public async Task Compile_EqOnString_MatchesRowsCaseInsensitively_EndToEnd()
    {
        // The parameterization must not change which rows match: the default mode stays case-insensitive.
        _context.TestEntities.Add(new TestEntity { Id = 1, Name = "Alice" });
        _context.TestEntities.Add(new TestEntity { Id = 2, Name = "Bob" });
        await _context.SaveChangesAsync();

        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonValue("alice") };

        var matches = await _context.TestEntities.Where(FilterCompiler.Compile<TestEntity>(node, _fieldMap)).ToListAsync();

        matches.Should().ContainSingle().Which.Name.Should().Be("Alice");
    }

    [Fact]
    public void Compile_CollationMode_DoesNotWrapColumnInLower()
    {
        // Sargability: the opt-in collation mode must leave the column bare (no LOWER wrapping) so an
        // index on the column stays usable, while still parameterizing the value.
        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Contains, Value = JsonValue("li") };

        var sql = Statement(_context.TestEntities
            .Where(FilterCompiler.Compile<TestEntity>(node, _fieldMap, FilterStringMatch.Collation))
            .ToQueryString());

        sql.Should().NotContainEquivalentOf("lower(");
        sql.Should().Contain("@");
    }

    [Theory]
    [InlineData(FilterOperator.Eq, "IS NULL")]
    [InlineData(FilterOperator.Neq, "IS NOT NULL")]
    public void Compile_NullValue_EmitsANullTestWithNoParameter(FilterOperator op, string expected)
    {
        // The fix is about the SQL, so this is where it has to be read: the null must become a null
        // test on the column, not a parameter the database compares the column against, and not the
        // LOWER(COALESCE(col,'')) = '' the string path would have produced. Mapped onto TenantId
        // because that is the one nullable column here: over a column the model knows is NOT NULL,
        // Entity Framework Core folds the test to a constant before it reaches the statement.
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Name).MapsTo(e => e.TenantId).AllowFilter(FilterOperator.Eq, FilterOperator.Neq);
        var node = new ComparisonNode { Field = "Name", Op = op, Value = JsonNull() };

        var sql = Statement(_context.TestEntities
            .Where(FilterCompiler.Compile<TestEntity>(node, builder.BuildFieldMap()))
            .ToQueryString());

        sql.Should().ContainEquivalentOf(expected);
        sql.Should().NotContain("@");
        sql.Should().NotContainEquivalentOf("lower(");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
