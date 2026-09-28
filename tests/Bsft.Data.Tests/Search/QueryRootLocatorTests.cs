using Bsft.Data.Search;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Search;

/// <summary>
/// How a search provider reaches the model without holding a <c>DbContext</c>. The two provider
/// packages depend on this answer: <c>Bsft.Data.Sqlite</c> builds its FTS5 index query on the model
/// found here, and both providers check their mapping against it before composing anything.
/// </summary>
public class QueryRootLocatorTests : IDisposable
{
    private readonly CandidateContext _context;

    public QueryRootLocatorTests()
    {
        var options = new DbContextOptionsBuilder<CandidateContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CandidateContext(options);
    }

    [Fact]
    public void TheModelIsFoundOnASet()
    {
        var model = QueryRootLocator.FindModel(((IQueryable<Candidate>)_context.Candidates).Expression);

        model.Should().NotBeNull();
        model!.FindEntityType(typeof(Candidate)).Should().NotBeNull();
    }

    [Fact]
    public void TheModelIsFoundUnderComposedOperators()
    {
        // The root sits at the bottom of whatever the caller composed — the executor has already
        // applied scoping, filtering and sorting by the time a provider sees the queryable.
        var composed = _context.Candidates
            .Where(c => c.TenantId == "t1")
            .OrderBy(c => c.Name)
            .Select(c => c);

        QueryRootLocator.FindModel(composed.Expression).Should().NotBeNull();
    }

    [Fact]
    public void AQueryableThatIsNotEfCoresHasNoModel()
    {
        // Answered as "no model" rather than by throwing, so the caller can say what it wanted the
        // model for. A provider that silently carried on here would search nothing.
        QueryRootLocator.FindModel(new List<Candidate>().AsQueryable().Expression).Should().BeNull();
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
