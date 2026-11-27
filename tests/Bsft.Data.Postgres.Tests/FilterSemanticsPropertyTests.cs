using System.Globalization;
using System.Text.Json;
using Bsft.Core;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Filtering;
using Bsft.Data.Postgres.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Bsft.Data.Postgres.Tests;

/// <summary>
/// Property test of the missing-value filter semantics: for random filter trees over declared nullable columns
/// and jsonb dynamic fields, including <c>in</c>, <c>eq null</c>, <c>neq null</c> and <c>neq</c> on missing
/// keys and stored JSON nulls, the in-memory evaluation and PostgreSQL return the same rows.
/// </summary>
/// <remarks>
/// The seed is fixed so CI is reproducible and logged with every run; <c>BSFT_PROPERTY_SEED</c> runs
/// another one. A failure names the seed, the case and the filter as JSON.
/// </remarks>
public sealed class FilterSemanticsPropertyTests : IAsyncLifetime
{
    private const int DefaultSeed = 20261027;
    private const int CaseCount = 10_000;

    private readonly PostgresDatabaseFixture _fixture = new();
    private readonly ITestOutputHelper _output;
    private SemanticsContext? _context;
    private List<SemanticsRow> _rows = [];

    public FilterSemanticsPropertyTests(ITestOutputHelper output) => _output = output;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        var connectionString = await _fixture.CreateDatabaseAsync("filter_semantics");
        _context = new SemanticsContext(new DbContextOptionsBuilder<SemanticsContext>().UseNpgsql(connectionString).Options);
        _rows = await SemanticsData.SeedAsync(_context);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();

        await _fixture.DisposeAsync();
    }

    [PostgresFact]
    public async Task Random_filters_select_the_same_rows_in_memory_and_in_PostgreSQL()
    {
        var seed = Seed();
        _output.WriteLine($"Seed {seed}, {CaseCount} cases, {_rows.Count} rows (BSFT_PROPERTY_SEED overrides the seed).");

        var disagreement = await FindDisagreementAsync(new JsonbCustomResolver(), seed, CaseCount);

        disagreement.Should().BeNull();
    }

    [PostgresFact]
    public async Task Counter_check_a_presence_test_that_counts_a_stored_json_null_is_caught()
    {
        var disagreement = await FindDisagreementAsync(new JsonbCustomResolver(jsonNullIsPresent: true), DefaultSeed, 2_000);

        disagreement.Should().NotBeNull("a wrong presence test must make the two evaluators disagree");
        _output.WriteLine($"Counter-check found: {disagreement}");
    }

    private static int Seed() =>
        int.TryParse(Environment.GetEnvironmentVariable("BSFT_PROPERTY_SEED"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)
            ? seed
            : DefaultSeed;

    /// <summary>
    /// Runs <paramref name="cases"/> random filters through both evaluators and describes the first one
    /// on which they disagree, or returns <see langword="null"/>.
    /// </summary>
    private async Task<string?> FindDisagreementAsync(IDynamicFieldResolver<SemanticsRow> sqlResolver, int seed, int cases)
    {
        var memoryMap = SemanticsData.FieldMap(new InMemoryCustomResolver());
        var sqlMap = SemanticsData.FieldMap(sqlResolver);
        var random = new Random(seed);
        var compared = 0;

        for (var i = 0; i < cases; i++)
        {
            var filter = SemanticsData.RandomFilter(random, depth: 3);

            int[] inMemory;
            try
            {
                var predicate = filter.Compile(memoryMap).Compile();
                inMemory = [.. _rows.Where(predicate).Select(r => r.Id).Order()];
            }
            catch (ValidationException)
            {
                // Refused in memory: the SQL map refuses the same tree, or that is a disagreement too.
                var refusedInSql = false;
                try
                {
                    filter.Compile(sqlMap);
                }
                catch (ValidationException)
                {
                    refusedInSql = true;
                }

                if (!refusedInSql)
                    return Describe(seed, i, filter, "refused in memory only");

                continue;
            }

            var sql = await _context!.Rows.AsNoTracking().Where(filter.Compile(sqlMap)).Select(r => r.Id).OrderBy(id => id).ToArrayAsync();
            compared++;

            if (!inMemory.SequenceEqual(sql))
                return Describe(seed, i, filter, $"in memory [{string.Join(',', inMemory)}], SQL [{string.Join(',', sql)}]");
        }

        _output.WriteLine($"{compared} of {cases} cases compared, the rest refused by both.");
        return null;
    }

    private static string Describe(int seed, int index, FilterNode filter, string detail) =>
        $"seed {seed}, case {index}: {detail}; filter {JsonSerializer.Serialize(filter)}";
}
