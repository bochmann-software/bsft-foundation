using System.Linq.Expressions;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

/// <summary>
/// How sort criteria become an ordering: each criterion after the first refines the ones before it,
/// the key is appended only when the criteria do not already order by it, and a mapped expression
/// keeps whatever lambdas it carries of its own.
/// </summary>
public class QuerySortingTests : IDisposable
{
    private readonly TestDbContext _context;

    public QuerySortingTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new TestDbContext(options);
        _context.TestEntities.AddRange(
            new TestEntity { Id = 1, Age = 30, Name = "b" },
            new TestEntity { Id = 2, Age = 20, Name = "a" },
            new TestEntity { Id = 3, Age = 30, Name = "a" },
            new TestEntity { Id = 4, Age = 20, Name = "b" });
        _context.SaveChanges();
    }

    [Theory]
    [InlineData(SortDirection.Asc, SortDirection.Desc, new[] { 4, 2, 1, 3 })]
    [InlineData(SortDirection.Desc, SortDirection.Asc, new[] { 3, 1, 2, 4 })]
    public async Task ExecuteAsync_WithTwoSortCriteria_OrdersByTheSecondWithinTheFirst(
        SortDirection ageDirection, SortDirection nameDirection, int[] expectedIds)
    {
        var executor = new QueryExecutor<TestEntity, TestDto>(new TestQueryConfiguration());

        var result = await executor.ExecuteAsync(
            _context.TestEntities,
            new PagedRequest(
                Sort: [new SortItem("Age", ageDirection), new SortItem("Name", nameDirection)],
                Page: 1,
                PageSize: 10));

        result.Items.Select(e => e.Id).Should().Equal(expectedIds);
    }

    [Fact]
    public async Task ExecuteAsync_WithAnEmptySortList_FallsBackToTheConfiguredDefaultSort()
    {
        // An empty list states no order, exactly like an absent one; TestQueryConfiguration sorts by
        // Name by default and the key breaks the tie.
        var executor = new QueryExecutor<TestEntity, TestDto>(new TestQueryConfiguration());

        var result = await executor.ExecuteAsync(
            _context.TestEntities.OrderByDescending(e => e.Id),
            new PagedRequest(Sort: [], Page: 1, PageSize: 10));

        result.Items.Select(e => e.Id).Should().Equal(2, 3, 1, 4);
    }

    [Fact]
    public void Apply_WithNoCriteria_ReturnsTheSourceUntouched()
    {
        var source = _context.TestEntities.AsQueryable();

        QuerySorting.Apply(source, [], FieldMap(new TestQueryConfiguration())).Should().BeSameAs(source);
    }

    [Theory]
    [InlineData("Id", 0)]
    [InlineData("Age", 1)]
    public void Apply_ByWhetherTheCriteriaSortOnTheKey_AppendsTheKeyOnlyWhenTheyDoNot(string field, int expectedThenBys)
    {
        var sorted = QuerySorting.Apply(
            Array.Empty<TestEntity>().AsQueryable(),
            [new SortItem(field, SortDirection.Desc)],
            FieldMap(new KeyAndAgeConfiguration()));

        ThenByCalls(sorted.Expression).Should().Be(expectedThenBys);
    }

    [Fact]
    public void Apply_WithASortOnAKeyDeclaredByABaseType_RecognisesItAsTheKey()
    {
        // The mapped lambda reads the key through the base type that declares it, while the key
        // convention finds it on the entity: two views of one member, which is still the key.
        var sorted = QuerySorting.Apply(
            Array.Empty<DerivedKeyEntity>().AsQueryable(),
            [new SortItem("Id", SortDirection.Desc)],
            FieldMap(new DerivedKeyConfiguration()));

        ThenByCalls(sorted.Expression).Should().Be(0);
    }

    [Fact]
    public void Apply_WithASortFieldCarryingALambdaOfItsOwn_KeepsThatLambdasParameter()
    {
        var source = new[]
        {
            new TestEntity { Id = 1, Name = "aaa" },
            new TestEntity { Id = 2, Name = "a" },
            new TestEntity { Id = 3, Name = "aa" },
        }.AsQueryable();

        var sorted = QuerySorting.Apply(
            source,
            [new SortItem("Age", SortDirection.Asc)],
            FieldMap(new CountedLetterConfiguration()));

        sorted.Select(e => e.Id).Should().Equal(2, 3, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveRankTieBreakers_WithADefaultSort_AppendsTheKeyUnlessTheDefaultSortIsTheKey(bool defaultSortIsKey)
    {
        Expression<Func<TestEntity, object?>> byName = e => e.Name;
        Expression<Func<TestEntity, object?>> byId = e => e.Id;

        var steps = QuerySorting.ResolveRankTieBreakers<TestEntity>(
            defaultSortIsKey ? byId : byName, SortDirection.Desc, byName);

        steps.Select(step => (EntityKeyResolver.FindMember(step.Selector.Body)!.Name, step.Direction))
            .Should().Equal(defaultSortIsKey
                ? [("Id", SortDirection.Desc)]
                : [("Name", SortDirection.Desc), ("Id", SortDirection.Asc)]);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private static QueryFieldMap<TEntity> FieldMap<TEntity, TDto>(QueryConfigurationBase<TEntity, TDto> configuration)
    {
        var builder = new QueryConfigurationBuilder<TEntity, TDto>();
        configuration.Configure(builder);
        return builder.BuildFieldMap();
    }

    private static int ThenByCalls(Expression expression) =>
        expression is MethodCallExpression call
            ? (call.Method.Name.StartsWith("ThenBy", StringComparison.Ordinal) ? 1 : 0) + ThenByCalls(call.Arguments[0])
            : 0;

    /// <summary>The key and a non-unique field, both sortable.</summary>
    private sealed class KeyAndAgeConfiguration : QueryConfigurationBase<TestEntity, TestDto>
    {
        public override void Configure(QueryConfigurationBuilder<TestEntity, TestDto> builder)
        {
            builder.Field(d => d.Id).AllowSort();
            builder.Field(d => d.Age).AllowSort();
        }
    }

    /// <summary>A sortable field counting a letter, through a lambda of its own.</summary>
    private sealed class CountedLetterConfiguration : QueryConfigurationBase<TestEntity, TestDto>
    {
        public override void Configure(QueryConfigurationBuilder<TestEntity, TestDto> builder) =>
            builder.Field(d => d.Age).MapsTo(e => e.Name.Count(letter => letter == 'a')).AllowSort();
    }

    /// <summary>The key, read through the base type that declares it.</summary>
    private sealed class DerivedKeyConfiguration : QueryConfigurationBase<DerivedKeyEntity, DerivedKeyDto>
    {
        public override void Configure(QueryConfigurationBuilder<DerivedKeyEntity, DerivedKeyDto> builder) =>
            builder.Field(d => d.Id).MapsTo(e => e.Id).AllowSort();
    }
}
