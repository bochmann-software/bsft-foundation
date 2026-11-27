using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

/// <summary>
/// Tests for compiling the In operator on declared fields, with special attention to culture-aware string handling.
/// </summary>
public sealed class FilterSetCompilerTests
{
    private static JsonElement JsonArray(params object[] values) => JsonSerializer.SerializeToElement(values);

    [Fact]
    public void In_WithStringFieldAndLowerInvariantMatch_CompilesIntoFoldedOrOverEq()
    {
        // Set CultureInfo to Turkish (tr-TR) which has culture-specific casing rules
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

            var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
            builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq, FilterOperator.In);
            var map = builder.BuildFieldMap();

            // The `in` set on a string field with LowerInvariant matching
            var inNode = new ComparisonNode
            {
                Field = "Name",
                Op = FilterOperator.In,
                Value = JsonArray("I", "x")
            };

            var eqNode1 = new ComparisonNode
            {
                Field = "Name",
                Op = FilterOperator.Eq,
                Value = JsonSerializer.SerializeToElement("I")
            };

            var eqNode2 = new ComparisonNode
            {
                Field = "Name",
                Op = FilterOperator.Eq,
                Value = JsonSerializer.SerializeToElement("x")
            };

            var inPredicate = FilterCompiler.Compile<ExtensibleEntity>(inNode, map).Compile();
            var eq1Predicate = FilterCompiler.Compile<ExtensibleEntity>(eqNode1, map).Compile();
            var eq2Predicate = FilterCompiler.Compile<ExtensibleEntity>(eqNode2, map).Compile();

            var or = ((Expression<Func<ExtensibleEntity, bool>>)(e =>
                eq1Predicate(e) || eq2Predicate(e)
            )).Compile();

            // Test data: includes Turkish-specific characters
            var testEntities = new[]
            {
                new ExtensibleEntity { Name = "I" },      // Latin I
                new ExtensibleEntity { Name = "i" },      // Latin i
                new ExtensibleEntity { Name = "ı" },      // Turkish lowercase dotless ı
                new ExtensibleEntity { Name = "İ" },      // Turkish uppercase dotted I
                new ExtensibleEntity { Name = "x" },      // Latin x
                new ExtensibleEntity { Name = "" }        // empty string
            };

            // Both should give identical results row by row
            for (int i = 0; i < testEntities.Length; i++)
            {
                var inResult = inPredicate(testEntities[i]);
                var orResult = or(testEntities[i]);

                inResult.Should().Be(orResult,
                    $"Row {i} ({testEntities[i].Name ?? "null"}) should match identically with `in` and `or` of `eq`");
            }

            // At least one row should be true and at least one false
            var inResults = testEntities.Select(inPredicate).ToList();
            inResults.Should().Contain(true);
            inResults.Should().Contain(false);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void In_WithStringFieldLowerInvariant_FiltersCorrectly()
    {
        // String field with LowerInvariant compiles as an OrElse chain
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq, FilterOperator.In);
        var map = builder.BuildFieldMap();

        var inNode = new ComparisonNode
        {
            Field = "Name",
            Op = FilterOperator.In,
            Value = JsonArray("a", "b", "c")
        };

        var predicate = FilterCompiler.Compile<ExtensibleEntity>(inNode, map).Compile();

        predicate(new ExtensibleEntity { Name = "a" }).Should().BeTrue();
        predicate(new ExtensibleEntity { Name = "b" }).Should().BeTrue();
        predicate(new ExtensibleEntity { Name = "c" }).Should().BeTrue();
        predicate(new ExtensibleEntity { Name = "d" }).Should().BeFalse();
        predicate(new ExtensibleEntity { Name = "A" }).Should().BeTrue(); // case-insensitive
    }

    [Fact]
    public void In_WithIntField_FiltersCorrectly()
    {
        // Non-string field uses array-based contains
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.Field(d => d.Id).AllowFilter(FilterOperator.Eq, FilterOperator.In);
        var map = builder.BuildFieldMap();

        var inNode = new ComparisonNode
        {
            Field = "Id",
            Op = FilterOperator.In,
            Value = JsonArray(1, 2, 3)
        };

        var predicate = FilterCompiler.Compile<ExtensibleEntity>(inNode, map).Compile();

        predicate(new ExtensibleEntity { Id = 1 }).Should().BeTrue();
        predicate(new ExtensibleEntity { Id = 2 }).Should().BeTrue();
        predicate(new ExtensibleEntity { Id = 3 }).Should().BeTrue();
        predicate(new ExtensibleEntity { Id = 4 }).Should().BeFalse();
    }

    [Fact]
    public void In_WithStringFieldAndCollationMatch_FiltersCorrectly()
    {
        // String field with Collation uses contains on array
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.StringMatch(FilterStringMatch.Collation);
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq, FilterOperator.In);
        var map = builder.BuildFieldMap();

        var inNode = new ComparisonNode
        {
            Field = "Name",
            Op = FilterOperator.In,
            Value = JsonArray("a", "b", "c")
        };

        var predicate = FilterCompiler.Compile<ExtensibleEntity>(inNode, map).Compile();

        predicate(new ExtensibleEntity { Name = "a" }).Should().BeTrue();
        predicate(new ExtensibleEntity { Name = "b" }).Should().BeTrue();
        predicate(new ExtensibleEntity { Name = "c" }).Should().BeTrue();
        predicate(new ExtensibleEntity { Name = "d" }).Should().BeFalse();
        // With Collation, case sensitivity depends on the database/provider, not client-side folding
    }

    [Fact]
    public void In_WithEmptyStringInLowerInvariantMode_MatchesNullStringMember()
    {
        // In LowerInvariant mode, eq "" matches a null string member (as per documentation)
        // so in [""] should also match null
        var builder = new QueryConfigurationBuilder<PresenceEntity, PresenceEntity>();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq, FilterOperator.In);
        var map = builder.BuildFieldMap();

        var inNode = new ComparisonNode
        {
            Field = "Name",
            Op = FilterOperator.In,
            Value = JsonArray("")
        };

        var predicate = FilterCompiler.Compile<PresenceEntity>(inNode, map).Compile();

        var entityWithNull = new PresenceEntity { Name = null };
        var entityWithEmpty = new PresenceEntity { Name = "" };
        var entityWithValue = new PresenceEntity { Name = "nonempty" };

        predicate(entityWithNull).Should().BeTrue();
        predicate(entityWithEmpty).Should().BeTrue();
        predicate(entityWithValue).Should().BeFalse();
    }

    [Fact]
    public void In_WithThreeElements_AllowsCorrectMatches()
    {
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq, FilterOperator.In);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode
        {
            Field = "Name",
            Op = FilterOperator.In,
            Value = JsonArray("Alice", "Bob", "Charlie")
        };

        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        predicate(new ExtensibleEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new ExtensibleEntity { Name = "Bob" }).Should().BeTrue();
        predicate(new ExtensibleEntity { Name = "Charlie" }).Should().BeTrue();
        predicate(new ExtensibleEntity { Name = "Dave" }).Should().BeFalse();
        predicate(new ExtensibleEntity { Name = "" }).Should().BeFalse();
    }
}
