using Bsft.Data.Search;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Contracts.Search;
using Bsft.Data.Sqlite.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Sqlite.Tests;

/// <summary>
/// The case a migration meets in every environment that is not a fresh database: the table already
/// holds rows when the index is created.
/// </summary>
/// <remarks>
/// The triggers only carry changes made from the moment they exist, so an index created over a
/// populated table is empty and stays empty for every row nobody touches again. A search would then
/// return a strict, silent subset of the truth — which is why <c>RebuildFullTextIndex</c> exists and
/// why this is tested against the real engine rather than described in a comment.
/// </remarks>
public sealed class SqliteFullTextRebuildTests : IDisposable
{
    private static readonly Guid _berlin = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SearchTestContext _context;

    public SqliteFullTextRebuildTests()
    {
        _connection.Open();
        _context = new SearchTestContext(_connection);
        _context.Database.EnsureCreated();

        // Rows first, index second — the order a migration meets in production.
        _context.Candidates.Add(new Candidate { Id = 1, Name = "Anna Müller", TenantId = "t1", OrganizationUnitId = _berlin });
        _context.SaveChanges();
    }

    [Fact]
    public async Task AnIndexCreatedOverAPopulatedTableIsEmptyUntilItIsRebuilt()
    {
        FullTextSchema.Apply(_context);

        (await SearchAsync()).Should().BeEmpty();

        FullTextSchema.Apply(_context, rebuild: true);

        (await SearchAsync()).Should().BeEquivalentTo([1]);
    }

    [Fact]
    public async Task ApplyingTheSchemaTwiceIsHarmless()
    {
        // Every statement the helpers emit is re-runnable, so a migration re-applied after an
        // unrelated failure does not trip over its own leftovers.
        FullTextSchema.Apply(_context, rebuild: true);
        FullTextSchema.Apply(_context, rebuild: true);

        (await SearchAsync()).Should().BeEquivalentTo([1]);
    }

    private async Task<IReadOnlyList<int>> SearchAsync()
    {
        var executor = new SearchExecutor<Candidate, CandidateDto>(
            new CompiledSearchConfiguration<Candidate, CandidateDto>(new CandidateQueryConfiguration()),
            new NullTelemetry(),
            new SqliteSearchProvider(Options.Create(SqliteSearchProviderTests.SearchOptions())),
            new FixedScopeProvider(new SearchScopeState("t1", [_berlin])));

        var result = await executor.SearchAsync(
            _context.Candidates,
            new PagedRequest { Search = new SearchRequest("profile", "müll", SearchMode.Prefix) });

        return [.. result.Items.Select(i => i.Item.Id)];
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
