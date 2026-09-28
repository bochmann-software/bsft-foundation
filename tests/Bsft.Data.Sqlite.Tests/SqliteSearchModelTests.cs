using Bsft.Data.Search;
using Bsft.Data.Sqlite.Tests.Support;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Bsft.Data.MultiTenancy;
using Bsft.Data.Contracts.Search;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Xunit;

namespace Bsft.Data.Sqlite.Tests;

/// <summary>
/// What the provider does with a model that cannot carry an external-content FTS5 index. Every case
/// here is a deployment mistake that would otherwise surface as an engine error nobody can read, or —
/// worse, in the column-order case — as a query that runs and ranks wrong.
/// </summary>
public class SqliteSearchModelTests
{
    [Fact]
    public void ALongKeyIsJoinedWithoutACast()
    {
        using var context = new LongKeyContext();

        var sql = new SqliteSearchProvider(Options.Create(SqliteSearchProviderTests.SearchOptions()))
            .ApplySearch(context.Applicants, new ResolvedSearchRequest(Group<Applicant>(), "müller", SearchMode.Prefix))
            .ToQueryString();

        // An int key is widened to the index's rowid; a long key already is one.
        sql.Should().Contain("INNER JOIN").And.NotContain("CAST(");
    }

    [Fact]
    public void AnEntityWithoutAPrimaryKey_IsRefused()
    {
        using var context = new KeylessContext();

        Search(context.Applicants, Group<Applicant>())
            .Should().Throw<InvalidOperationException>().WithMessage("*no primary key*");
    }

    [Fact]
    public void ACompositeKey_IsRefused()
    {
        using var context = new CompositeKeyContext();

        Search(context.Applicants, Group<Applicant>())
            .Should().Throw<InvalidOperationException>().WithMessage("*composite primary key*");
    }

    [Fact]
    public void ANonIntegerKey_IsRefused()
    {
        using var context = new GuidKeyContext();

        Search(context.Referrals, Group<Referral>())
            .Should().Throw<InvalidOperationException>().WithMessage("*Guid primary key*");
    }

    [Fact]
    public void AnUnmappedEntity_IsRefused()
    {
        using var context = new LongKeyContext();

        // A queryable of a type this model does not know, projected out of one it does. There is no
        // table behind it, so there is no rowid the index could be joined by.
        var projected = context.Applicants.Select(a => new Candidate { Id = (int)a.Id, Name = a.Name });

        Search(projected, SqliteSearchProviderTests.Group())
            .Should().Throw<InvalidOperationException>().WithMessage("*not mapped*");
    }

    [Fact]
    public void AnIndexMappedWithoutItsColumnList_IsRefused()
    {
        using var context = new UnannotatedIndexContext();

        Search(context.Candidates, SqliteSearchProviderTests.Group())
            .Should().Throw<InvalidOperationException>().WithMessage("*without its column list*");
    }

    [Fact]
    public void AQueryableCarryingAnEfExpressionOnAForeignProvider_IsRefused()
    {
        using var context = new SearchTestContext();

        // The EF root is there, so the model is found — but the provider cannot execute anything,
        // and building the index query on it would produce a queryable nobody can run.
        var foreign = new EnumerableQuery<Candidate>(((IQueryable<Candidate>)context.Candidates).Expression);

        Search(foreign, SqliteSearchProviderTests.Group())
            .Should().Throw<InvalidOperationException>().WithMessage("*not an EF Core query*");
    }

    [Fact]
    public void TheFts5MarkerMethodsAreNeverCalled()
    {
        FluentActions.Invoking(() => SqliteFullTextFunctions.IsMatch("q", "col"))
            .Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => SqliteFullTextFunctions.Bm25("col", "1.0"))
            .Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void AnApplicationCanMapMoreThanOneIndex()
    {
        // Two searchable entities is the normal case, and the second call registers the same two
        // FTS5 functions again. Each index is its own shared-type mapping, named after its table.
        using var context = new TwoIndexContext();

        context.Model.FindEntityType("Candidates_fts").Should().NotBeNull();
        context.Model.FindEntityType("Applicants_fts").Should().NotBeNull();

        var sql = new SqliteSearchProvider(
                Options.Create(new BsftDataSqliteOptions()
                    .HasFullTextIndex("applicants", "Applicants_fts")
                    .AcceptsUnstemmedLanguage("de")))
            .ApplySearch(context.Applicants, new ResolvedSearchRequest(Group<Applicant>("applicants"), "müller", SearchMode.Prefix))
            .ToQueryString();

        sql.Should().Contain("\"Applicants_fts\"").And.NotContain("\"Candidates_fts\"");
    }

    [Fact]
    public void HasBsftFullTextIndex_ValidatesItsArguments()
    {
        FluentActions.Invoking(() => ModelBuilderExtensions.HasBsftFullTextIndex(null!, "Candidates_fts", "Name"))
            .Should().Throw<ArgumentNullException>().WithParameterName("modelBuilder");
        FluentActions.Invoking(() => new ModelBuilder().HasBsftFullTextIndex(" ", "Name"))
            .Should().Throw<ArgumentException>().WithParameterName("indexTable");
        FluentActions.Invoking(() => new ModelBuilder().HasBsftFullTextIndex("Candidates_fts"))
            .Should().Throw<ArgumentException>().WithParameterName("columns");
        FluentActions.Invoking(() => new ModelBuilder().HasBsftFullTextIndex("Candidates_fts", null!))
            .Should().Throw<ArgumentException>().WithParameterName("columns");
    }

    [Fact]
    public void Bm25WeightsThatAreNotConstantNumbers_AreRefusedByTheTranslation()
    {
        using var context = new SearchTestContext();
        var translate = Translation(context, nameof(SqliteFullTextFunctions.Bm25));
        var column = Constant("Candidates_fts");

        // Only the provider is meant to call bm25, and it always passes a constant list of numbers.
        // Anything else would silently produce a statement bm25 rejects at plan time.
        FluentActions.Invoking(() => translate([column, Constant(1)]))
            .Should().Throw<NotSupportedException>().WithMessage("*constant*");
        FluentActions.Invoking(() => translate([column, Constant("heavy")]))
            .Should().Throw<NotSupportedException>().WithMessage("*not a column weight*");
    }

    private static Func<IReadOnlyList<SqlExpression>, SqlExpression> Translation(DbContext context, string method)
    {
        var function = context.Model.GetDbFunctions()
            .Single(f => string.Equals(f.MethodInfo?.Name, method, StringComparison.Ordinal));

        return function.Translation!;
    }

    private static SqlConstantExpression Constant(object value) => new(value, typeMapping: null);

    private static Func<IQueryable<RankedItem<TEntity>>> Search<TEntity>(IQueryable<TEntity> source, SearchGroupConfiguration group) =>
        () => new SqliteSearchProvider(Options.Create(SqliteSearchProviderTests.SearchOptions()))
            .ApplySearch(source, new ResolvedSearchRequest(group, "müller", SearchMode.Prefix));

    private static SearchGroupConfiguration Group<TEntity>(string groupName = "profile")
    {
        Expression<Func<TEntity, string?>> name = e => EF.Property<string?>(e!, "Name");
        Expression<Func<TEntity, string?>> notes = e => EF.Property<string?>(e!, "Notes");

        return new SearchGroupConfiguration(
            groupName,
            typeof(TEntity),
            [new SearchField("Name", name, 2.0), new SearchField("Notes", notes, 1.0)],
            SearchMode.Prefix,
            "de");
    }
}
