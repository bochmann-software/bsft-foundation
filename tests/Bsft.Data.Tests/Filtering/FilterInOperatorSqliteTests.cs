using System.Text.Json;
using Bsft.Core;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

public sealed class FilterInOperatorSqliteTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContext _context;
    private readonly QueryFieldMap<TestEntity> _fieldMap;

    public FilterInOperatorSqliteTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options;
        _context = new TestDbContext(options);
        _context.Database.EnsureCreated();

        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Age).AllowFilter(FilterOperator.In);
        _fieldMap = builder.BuildFieldMap();
    }

    private static JsonElement JsonArray(params object[] values) => JsonSerializer.SerializeToElement(values);

    private static string Statement(string sql)
    {
        var index = sql.IndexOf("SELECT", StringComparison.Ordinal);
        return index < 0 ? sql : sql[index..];
    }

    [Fact]
    public void Compile_TwoDistinctValueSets_ProduceSameParameterizedSqlShape()
    {
        // Parameterization: two different value sets of equal length must yield the same SQL shape
        var set1 = new ComparisonNode { Field = "Age", Op = FilterOperator.In, Value = JsonArray(20, 30) };
        var set2 = new ComparisonNode { Field = "Age", Op = FilterOperator.In, Value = JsonArray(40, 50) };

        var sql1 = Statement(_context.TestEntities.Where(FilterCompiler.Compile<TestEntity>(set1, _fieldMap)).ToQueryString());
        var sql2 = Statement(_context.TestEntities.Where(FilterCompiler.Compile<TestEntity>(set2, _fieldMap)).ToQueryString());

        sql1.Should().Be(sql2);
        sql1.Should().Contain("@");
        // No literal values in the SQL
        sql1.Should().NotContainEquivalentOf("20");
        sql1.Should().NotContainEquivalentOf("30");
        sql1.Should().NotContainEquivalentOf("40");
        sql1.Should().NotContainEquivalentOf("50");
    }

    [Fact]
    public async Task Compile_InOperator_ReturnsCorrectRowsFromSqlite()
    {
        _context.TestEntities.AddRange(
            new TestEntity { Id = 1, Age = 20 },
            new TestEntity { Id = 2, Age = 25 },
            new TestEntity { Id = 3, Age = 30 },
            new TestEntity { Id = 4, Age = 35 },
            new TestEntity { Id = 5, Age = 40 });
        await _context.SaveChangesAsync();

        var node = new ComparisonNode { Field = "Age", Op = FilterOperator.In, Value = JsonArray(20, 30, 40) };

        var matches = await _context.TestEntities.Where(FilterCompiler.Compile<TestEntity>(node, _fieldMap)).ToListAsync();

        matches.Should().HaveCount(3);
        var ages = matches.Select(e => e.Age).ToList();
        ages.Should().Contain(20);
        ages.Should().Contain(30);
        ages.Should().Contain(40);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
