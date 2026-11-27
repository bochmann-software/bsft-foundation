using System.Text.Json;
using Bsft.Core;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Filtering;
using Bsft.Data.Postgres.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Bsft.Data.Postgres.Tests;

/// <summary>
/// The missing-value table of the filter semantics (a missing key and a stored JSON null are both "not set"), written out on four rows,
/// once for a declared nullable column (<c>Size</c>) and once for a jsonb dynamic field (<c>custom.n</c>),
/// each asserted in memory and in PostgreSQL.
/// </summary>
/// <remarks>
/// Row 1 holds 5, row 2 holds a null (a SQL <c>NULL</c>, a stored JSON <c>null</c>), row 3 holds nothing
/// (<c>NULL</c>, a missing key), row 4 holds 3.
/// </remarks>
public sealed class FilterSemanticsPostgresTests : IAsyncLifetime
{
    private readonly PostgresDatabaseFixture _fixture = new();
    private SemanticsContext? _context;
    private List<SemanticsRow> _rows = [];

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        if (!_fixture.IsAvailable)
            return;

        var connectionString = await _fixture.CreateDatabaseAsync("filter_examples");
        _context = new SemanticsContext(new DbContextOptionsBuilder<SemanticsContext>().UseNpgsql(connectionString).Options);
        await _context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();

        _context.Rows.AddRange(
            new SemanticsRow { Id = 1, Size = 5, Custom = JsonDocument.Parse("{\"n\": 5}") },
            new SemanticsRow { Id = 2, Size = null, Custom = JsonDocument.Parse("{\"n\": null}") },
            new SemanticsRow { Id = 3, Size = null, Custom = JsonDocument.Parse("{\"s\": \"a\"}") },
            new SemanticsRow { Id = 4, Size = 3, Custom = JsonDocument.Parse("{\"n\": 3}") });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        _rows = await _context.Rows.AsNoTracking().ToListAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();

        await _fixture.DisposeAsync();
    }

    public static TheoryData<string, FilterOperator, string, int[]> MissingValueTable()
    {
        var table = new TheoryData<string, FilterOperator, string, int[]>();
        foreach (var field in new[] { "Size", "custom.n" })
        {
            table.Add(field, FilterOperator.Eq, "null", [2, 3]);
            table.Add(field, FilterOperator.Neq, "null", [1, 4]);
            table.Add(field, FilterOperator.Neq, "3", [1, 2, 3]);
            table.Add(field, FilterOperator.Eq, "3", [4]);
            table.Add(field, FilterOperator.Gt, "4", [1]);
            table.Add(field, FilterOperator.Lt, "4", [4]);
            table.Add(field, FilterOperator.In, "[3, 5]", [1, 4]);
            table.Add(field, FilterOperator.In, "[7]", []);
        }

        return table;
    }

    [PostgresTheory]
    [MemberData(nameof(MissingValueTable))]
    public async Task A_missing_value_means_the_same_in_memory_and_in_PostgreSQL(string field, FilterOperator op, string value, int[] expected)
    {
        var filter = new ComparisonNode { Field = field, Op = op, Value = JsonDocument.Parse(value).RootElement.Clone() };

        var (inMemory, sql) = await RunAsync(filter);

        inMemory.Should().Equal(expected);
        sql.Should().Equal(expected);
    }

    [PostgresTheory]
    [InlineData("Size")]
    [InlineData("custom.n")]
    public async Task Neq_null_selects_what_presence_selects(string field)
    {
        var neqNull = await RunAsync(new ComparisonNode { Field = field, Op = FilterOperator.Neq, Value = JsonDocument.Parse("null").RootElement.Clone() });
        var present = await RunAsync(new PresenceNode { Field = field });

        neqNull.InMemory.Should().Equal(present.InMemory).And.Equal(1, 4);
        neqNull.Sql.Should().Equal(present.Sql).And.Equal(1, 4);
    }

    [Theory]
    [InlineData("Size", "[]", FilterInValues.EmptyErrorCode)]
    [InlineData("custom.n", "[]", FilterInValues.EmptyErrorCode)]
    [InlineData("Size", "[3, null]", FilterInValues.NullElementErrorCode)]
    [InlineData("custom.n", "[3, null]", FilterInValues.NullElementErrorCode)]
    public void An_empty_set_or_a_null_element_is_refused_for_memory_and_for_SQL(string field, string value, string errorCode)
    {
        var filter = new ComparisonNode { Field = field, Op = FilterOperator.In, Value = JsonDocument.Parse(value).RootElement.Clone() };

        var inMemory = () => filter.Compile(SemanticsData.FieldMap(new InMemoryCustomResolver()));
        var sql = () => filter.Compile(SemanticsData.FieldMap(new JsonbCustomResolver()));

        inMemory.Should().Throw<ValidationException>().Which.ErrorCode.Should().Be(errorCode);
        sql.Should().Throw<ValidationException>().Which.ErrorCode.Should().Be(errorCode);
    }

    [PostgresFact]
    public async Task In_on_a_declared_column_sends_its_values_as_parameters()
    {
        var map = SemanticsData.FieldMap(new JsonbCustomResolver());
        var first = new ComparisonNode { Field = "Size", Op = FilterOperator.In, Value = JsonDocument.Parse("[3, 5]").RootElement.Clone() };
        var second = new ComparisonNode { Field = "Size", Op = FilterOperator.In, Value = JsonDocument.Parse("[11, 13]").RootElement.Clone() };

        var firstSql = _context!.Rows.Where(first.Compile(map)).ToQueryString();
        var secondSql = _context.Rows.Where(second.Compile(map)).ToQueryString();

        Statement(firstSql).Should().Be(Statement(secondSql));
        Statement(firstSql).Should().Contain("@").And.NotContain("11").And.NotContain("13");
    }

    [Theory]
    [InlineData("Size")]
    [InlineData("custom.n")]
    public void A_set_over_the_configured_limit_is_refused_for_memory_and_for_SQL(string field)
    {
        var filter = new ComparisonNode { Field = field, Op = FilterOperator.In, Value = JsonDocument.Parse("[0, 1, 3, 5]").RootElement.Clone() };

        var inMemory = () => filter.Compile(SemanticsData.FieldMap(new InMemoryCustomResolver(), maxInValues: 3));
        var sql = () => filter.Compile(SemanticsData.FieldMap(new JsonbCustomResolver(), maxInValues: 3));

        inMemory.Should().Throw<ValidationException>().Which.ErrorCode.Should().Be(FilterInValues.TooManyErrorCode);
        sql.Should().Throw<ValidationException>().Which.ErrorCode.Should().Be(FilterInValues.TooManyErrorCode);
    }

    [PostgresTheory]
    [InlineData("Size")]
    [InlineData("custom.n")]
    public async Task A_set_at_the_configured_limit_selects_the_same_rows_in_memory_and_in_PostgreSQL(string field)
    {
        // Counter-check of the refusal above: one value fewer, same limit, and the set runs on both sides.
        var filter = new ComparisonNode { Field = field, Op = FilterOperator.In, Value = JsonDocument.Parse("[1, 3, 5]").RootElement.Clone() };

        var inMemory = _rows.Where(filter.Compile(SemanticsData.FieldMap(new InMemoryCustomResolver(), maxInValues: 3)).Compile())
            .Select(r => r.Id).Order().ToArray();
        var sql = await _context!.Rows.AsNoTracking().Where(filter.Compile(SemanticsData.FieldMap(new JsonbCustomResolver(), maxInValues: 3)))
            .Select(r => r.Id).OrderBy(id => id).ToArrayAsync();

        inMemory.Should().NotBeEmpty().And.Equal(sql);
    }

    [PostgresFact]
    public async Task In_on_a_dynamic_field_built_from_eq_selects_what_an_or_over_eq_selects()
    {
        var inFilter = new ComparisonNode { Field = "custom.s", Op = FilterOperator.In, Value = JsonDocument.Parse("[\"a\", \"b\"]").RootElement.Clone() };
        var orFilter = new OrNode
        {
            Or =
            [
                new ComparisonNode { Field = "custom.s", Op = FilterOperator.Eq, Value = JsonDocument.Parse("\"a\"").RootElement.Clone() },
                new ComparisonNode { Field = "custom.s", Op = FilterOperator.Eq, Value = JsonDocument.Parse("\"b\"").RootElement.Clone() },
            ],
        };

        var set = await RunAsync(inFilter);
        var or = await RunAsync(orFilter);

        set.InMemory.Should().NotBeEmpty().And.Equal(set.Sql);
        set.InMemory.Should().Equal(or.InMemory);
        set.Sql.Should().Equal(or.Sql);
    }

    private async Task<(int[] InMemory, int[] Sql)> RunAsync(FilterNode filter)
    {
        var inMemory = _rows.Where(filter.Compile(SemanticsData.FieldMap(new InMemoryCustomResolver())).Compile())
            .Select(r => r.Id).Order().ToArray();
        var sql = await _context!.Rows.AsNoTracking().Where(filter.Compile(SemanticsData.FieldMap(new JsonbCustomResolver())))
            .Select(r => r.Id).OrderBy(id => id).ToArrayAsync();
        return (inMemory, sql);
    }

    /// <summary>The SQL statement without the parameter declarations <c>ToQueryString</c> puts before it.</summary>
    private static string Statement(string sql)
    {
        var index = sql.IndexOf("SELECT", StringComparison.Ordinal);
        return index < 0 ? sql : sql[index..];
    }
}
