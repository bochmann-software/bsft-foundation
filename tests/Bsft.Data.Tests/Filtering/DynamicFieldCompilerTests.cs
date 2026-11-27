using System.Linq.Expressions;
using System.Text.Json;
using Bsft.Core;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

/// <summary>
/// Tests for how dynamic fields are compiled by the filter compiler, including missing values,
/// presence semantics, and the composition of operators from basic predicates.
/// </summary>
public sealed class DynamicFieldCompilerTests
{
    private static JsonElement JsonValue(object value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement JsonNull() => JsonSerializer.SerializeToElement<object?>(null);

    private static JsonElement JsonArray(params object[] values) => JsonSerializer.SerializeToElement(values);

    private sealed class TestDynamicFieldResolver : IDynamicFieldResolver<ExtensibleEntity>
    {
        private readonly HashSet<string> _requestedKeys = new();
        private readonly HashSet<string> _knownKeys;
        private readonly bool _includeInPredicate;

        public IReadOnlySet<string> RequestedKeys => _requestedKeys;

        public TestDynamicFieldResolver(params string[] knownKeys)
            : this(includeInPredicate: false, knownKeys)
        {
        }

        public TestDynamicFieldResolver(bool includeInPredicate, params string[] knownKeys)
        {
            _includeInPredicate = includeInPredicate;
            _knownKeys = new HashSet<string>(knownKeys ?? Array.Empty<string>());
        }

        public DynamicField<ExtensibleEntity>? Resolve(string key)
        {
            _requestedKeys.Add(key);

            // Return null for unknown keys
            if (_knownKeys.Count > 0 && !_knownKeys.Contains(key))
                return null;

            // Simple resolver: allows Eq, Gt, Lt, Contains, and optionally In
            var present = (Expression<Func<ExtensibleEntity, bool>>)(e => e.Custom.ContainsKey(key) && e.Custom[key] != null);
            var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
            {
                {
                    FilterOperator.Eq,
                    value => e =>
                        e.Custom.ContainsKey(key) && e.Custom[key] != null && e.Custom[key]!.Equals(value.GetInt32())
                },
                {
                    FilterOperator.Gt,
                    value => e =>
                        e.Custom.ContainsKey(key) && e.Custom[key] != null && (int)e.Custom[key]! > value.GetInt32()
                },
                {
                    FilterOperator.Lt,
                    value => e =>
                        e.Custom.ContainsKey(key) && e.Custom[key] != null && (int)e.Custom[key]! < value.GetInt32()
                },
                {
                    FilterOperator.Contains,
                    value => e =>
                        e.Custom.ContainsKey(key) && e.Custom[key] != null && e.Custom[key]!.ToString()!.Contains(value.GetString()!)
                }
            };

            // Add In predicate if requested (compose from Eq)
            if (_includeInPredicate)
            {
                predicates[FilterOperator.In] = ((Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>)(value => e =>
                    e.Custom.ContainsKey(key) && e.Custom[key] != null && e.Custom[key]!.Equals(value.GetInt32())))
                    .ToInPredicate();
            }

            var sort = (Expression<Func<ExtensibleEntity, object>>)(e => (object)(e.Custom.ContainsKey(key) ? e.Custom[key]! : 0));

            return new DynamicField<ExtensibleEntity>(present, predicates, sort);
        }
    }

    [Fact]
    public void Compile_DynamicFieldWithEqOperator_FiltersCorrectly()
    {
        var resolver = new TestDynamicFieldResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.age", Op = FilterOperator.Eq, Value = JsonValue(25) };

        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        var entityWithValue = new ExtensibleEntity { Custom = { { "age", 25 } } };
        var entityWithDifferentValue = new ExtensibleEntity { Custom = { { "age", 30 } } };
        var entityMissing = new ExtensibleEntity { Custom = new() };

        predicate(entityWithValue).Should().BeTrue();
        predicate(entityWithDifferentValue).Should().BeFalse();
        predicate(entityMissing).Should().BeFalse();
        resolver.RequestedKeys.Should().Contain("age");
    }

    [Fact]
    public void Compile_DynamicFieldWithPresenceNode_FiltersCorrectly()
    {
        var resolver = new TestDynamicFieldResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new PresenceNode { Field = "custom.status" };

        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        var entityWithValue = new ExtensibleEntity { Custom = { { "status", "active" } } };
        var entityWithNull = new ExtensibleEntity { Custom = { { "status", null } } };
        var entityMissing = new ExtensibleEntity { Custom = new() };

        predicate(entityWithValue).Should().BeTrue();
        predicate(entityWithNull).Should().BeFalse();
        predicate(entityMissing).Should().BeFalse();
    }

    [Fact]
    public void Compile_DynamicFieldWithEqNull_IsEquivalentToNotPresent()
    {
        var resolver = new TestDynamicFieldResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        // eq null is the canonical form of "not set"
        var node = new ComparisonNode { Field = "custom.field", Op = FilterOperator.Eq, Value = JsonNull() };

        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        var entityWithValue = new ExtensibleEntity { Custom = { { "field", 42 } } };
        var entityWithNull = new ExtensibleEntity { Custom = { { "field", null } } };
        var entityMissing = new ExtensibleEntity { Custom = new() };

        predicate(entityWithValue).Should().BeFalse();
        predicate(entityWithNull).Should().BeTrue();
        predicate(entityMissing).Should().BeTrue();
    }

    [Fact]
    public void Compile_DynamicFieldWithNeqNull_IsEquivalentToPresent()
    {
        var resolver = new TestDynamicFieldResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.field", Op = FilterOperator.Neq, Value = JsonNull() };

        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        var entityWithValue = new ExtensibleEntity { Custom = { { "field", 42 } } };
        var entityWithNull = new ExtensibleEntity { Custom = { { "field", null } } };
        var entityMissing = new ExtensibleEntity { Custom = new() };

        predicate(entityWithValue).Should().BeTrue();
        predicate(entityWithNull).Should().BeFalse();
        predicate(entityMissing).Should().BeFalse();
    }

    [Fact]
    public void Compile_DynamicFieldWithGtOperator_ComposesFromPresenceAndPredicate()
    {
        var resolver = new TestDynamicFieldResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.level", Op = FilterOperator.Gt, Value = JsonValue(10) };

        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        var entityAbove = new ExtensibleEntity { Custom = { { "level", 15 } } };
        var entityBelow = new ExtensibleEntity { Custom = { { "level", 5 } } };
        var entityMissing = new ExtensibleEntity { Custom = new() };

        predicate(entityAbove).Should().BeTrue();
        predicate(entityBelow).Should().BeFalse();
        predicate(entityMissing).Should().BeFalse();
    }

    [Fact]
    public void Compile_DynamicFieldWithNeqOperator_ComposesAsNotPresentOrNotPredicate()
    {
        var resolver = new TestDynamicFieldResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.flag", Op = FilterOperator.Neq, Value = JsonValue(99) };

        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        var entityEqual = new ExtensibleEntity { Custom = { { "flag", 99 } } };
        var entityNotEqual = new ExtensibleEntity { Custom = { { "flag", 50 } } };
        var entityMissing = new ExtensibleEntity { Custom = new() };

        // NOT present OR NOT predicate means: missing or different value both match
        predicate(entityEqual).Should().BeFalse();
        predicate(entityNotEqual).Should().BeTrue();
        predicate(entityMissing).Should().BeTrue();
    }

    [Fact]
    public void Compile_DynamicFieldWithoutInPredicate_ThrowsValidationException()
    {
        // The resolver does not provide an In predicate, so the compiler should refuse the In operator.
        var resolver = new TestDynamicFieldResolver(includeInPredicate: false);
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.priority", Op = FilterOperator.In, Value = JsonArray(1, 2, 3) };

        var ex = Assert.Throws<ValidationException>(() => FilterCompiler.Compile<ExtensibleEntity>(node, map));

        ex.Errors.Should().ContainKey("filter");
        ex.Errors["filter"][0].Should().Contain("not allowed");
    }

    [Fact]
    public void Compile_DynamicFieldWithInFromEq_FiltersCorrectly()
    {
        // The resolver provides an In predicate composed from its Eq predicate using ToInPredicate.
        var resolver = new TestDynamicFieldResolver(includeInPredicate: true);
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.priority", Op = FilterOperator.In, Value = JsonArray(1, 2, 3) };

        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        var entityInSet = new ExtensibleEntity { Custom = { { "priority", 2 } } };
        var entityNotInSet = new ExtensibleEntity { Custom = { { "priority", 5 } } };
        var entityMissing = new ExtensibleEntity { Custom = new() };

        predicate(entityInSet).Should().BeTrue();
        predicate(entityNotInSet).Should().BeFalse();
        predicate(entityMissing).Should().BeFalse();
    }

    [Fact]
    public void Compile_DynamicFieldUnknownKey_ThrowsValidationException()
    {
        var resolver = new TestDynamicFieldResolver("age"); // Only age is known
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.unknown", Op = FilterOperator.Eq, Value = JsonValue(42) };

        // The resolver returns null for unknown keys
        var ex = Assert.Throws<ValidationException>(() => FilterCompiler.Compile<ExtensibleEntity>(node, map));

        ex.Errors.Should().ContainKey("filter");
        ex.Errors["filter"][0].Should().Contain("Unknown");
    }

    [Fact]
    public void Compile_DynamicFieldNameOnlyPrefix_ThrowsValidationException()
    {
        var resolver = new TestDynamicFieldResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.", Op = FilterOperator.Eq, Value = JsonValue(42) };

        // "custom." is only the prefix, no key part
        var ex = Assert.Throws<ValidationException>(() => FilterCompiler.Compile<ExtensibleEntity>(node, map));

        ex.Errors.Should().ContainKey("filter");
    }

    [Fact]
    public void Compile_DeclaredFieldOutsideDynamicPrefix_CompilesDeclaredFieldWithoutAskingResolver()
    {
        var resolver = new TestDynamicFieldResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq);
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonValue("test") };

        // This should use the declared field, not the dynamic resolver, because Name does not start with "custom."
        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        var entity = new ExtensibleEntity { Name = "test" };
        predicate(entity).Should().BeTrue();

        // The resolver should never have been asked for Name
        resolver.RequestedKeys.Should().BeEmpty();

        // Counter-check: when asking for a field under the dynamic prefix, the resolver is asked
        var node2 = new ComparisonNode { Field = "custom.priority", Op = FilterOperator.Eq, Value = JsonValue(5) };
        _ = FilterCompiler.Compile<ExtensibleEntity>(node2, map);
        resolver.RequestedKeys.Should().Contain("priority");
    }

    [Fact]
    public void BuildFieldMap_WithDeclaredFieldStartingWithDynamicPrefix_ThrowsInvalidOperationException()
    {
        var resolver = new TestDynamicFieldResolver();
        var map = new QueryFieldMap<ExtensibleEntity>();
        // Add a field that starts with "custom." via a mock
        var fieldExpr = (Expression<Func<ExtensibleEntity, object?>>)(e => e.Id);
        map.Add("custom.field", fieldExpr, new HashSet<FilterOperator>(), false);

        // Now adding a dynamic prefix "custom." should throw because the declared field starts with it
        var ex = Assert.Throws<InvalidOperationException>(() =>
            map.AddDynamic("custom.", resolver));

        ex.Message.Should().Contain("starts with");
    }

    [Fact]
    public void BuildDynamicFields_WithOverlappingPrefixes_ThrowsArgumentException()
    {
        var resolver1 = new TestDynamicFieldResolver();
        var resolver2 = new TestDynamicFieldResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();

        builder.DynamicFields("custom.", resolver1);

        // Overlapping prefix should throw
        var ex = Assert.Throws<ArgumentException>(() =>
            builder.DynamicFields("custom.x.", resolver2));

        ex.ParamName.Should().Be("prefix");
        ex.Message.Should().Contain("overlaps");
    }

    [Fact]
    public void BuildDynamicFields_WithBlankPrefix_ThrowsArgumentException()
    {
        var resolver = new TestDynamicFieldResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();

        var ex = Assert.Throws<ArgumentException>(() =>
            builder.DynamicFields("   ", resolver));

        ex.ParamName.Should().Be("prefix");
        ex.Message.Should().Contain("required");
    }

    [Fact]
    public void BuildDynamicFields_WithNullResolver_ThrowsArgumentNullException()
    {
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();

        var ex = Assert.Throws<ArgumentNullException>(() =>
            builder.DynamicFields("custom.", null!));

        ex.ParamName.Should().Be("resolver");
    }

    [Fact]
    public void GetSortField_WithDynamicField_ReturnsSortKey()
    {
        var resolver = new TestDynamicFieldResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var sortField = map.GetSortField("custom.priority");

        sortField.Should().NotBeNull();
    }

    [Fact]
    public void GetSortField_WithUnknownDynamicField_ThrowsValidationException()
    {
        var resolver = new TestDynamicFieldResolver("age"); // Only age is known
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var ex = Assert.Throws<ValidationException>(() =>
            map.GetSortField("custom.unknown"));

        ex.Errors.Should().ContainKey("sort");
    }

    [Fact]
    public void GetSortField_WithDynamicFieldHavingNoSort_ThrowsValidationException()
    {
        var resolver = new NoSortDynamicFieldResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var ex = Assert.Throws<ValidationException>(() =>
            map.GetSortField("custom.notsortable"));

        ex.Errors.Should().ContainKey("sort");
        ex.Errors["sort"][0].Should().Contain("not allowed");
    }

    private sealed class NoSortDynamicFieldResolver : IDynamicFieldResolver<ExtensibleEntity>
    {
        public DynamicField<ExtensibleEntity>? Resolve(string key)
        {
            var present = (Expression<Func<ExtensibleEntity, bool>>)(e => e.Custom.ContainsKey(key));
            var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
            {
                { FilterOperator.Eq, v => e => e.Custom.ContainsKey(key) }
            };
            // No sort key (null)
            return new DynamicField<ExtensibleEntity>(present, predicates, null);
        }
    }
}
