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
/// Comprehensive semantic tests for dynamic field composition and error handling.
/// </summary>
public sealed class DynamicFieldSemanticsTests
{
    private static JsonElement JsonValue(object value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement JsonNull() => JsonSerializer.SerializeToElement<object?>(null);

    private static JsonElement JsonArray(params object?[] values) => JsonSerializer.SerializeToElement(values);

    /// <summary>
    /// Resolver with unsafe predicates that throw on missing/null keys.
    /// Proves the framework guards with presence checks.
    /// </summary>
    private sealed class UnsafePredicateResolver : IDynamicFieldResolver<ExtensibleEntity>
    {
        public DynamicField<ExtensibleEntity>? Resolve(string key)
        {
            var present = (Expression<Func<ExtensibleEntity, bool>>)(e => e.Custom.ContainsKey(key) && e.Custom[key] != null);
            var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
            {
                {
                    FilterOperator.Eq,
                    value => e => (int)e.Custom[key]! == value.GetInt32()
                },
                {
                    FilterOperator.Contains,
                    value => e => e.Custom[key]!.ToString()!.Contains(value.GetString()!)
                },
                {
                    FilterOperator.Gt,
                    value => e => (int)e.Custom[key]! > value.GetInt32()
                }
            };
            var sort = (Expression<Func<ExtensibleEntity, object>>)(e => (object)(e.Custom.ContainsKey(key) ? e.Custom[key]! : 0));
            return new DynamicField<ExtensibleEntity>(present, predicates, sort);
        }
    }

    [Fact]
    public void Compile_PositiveEqOnUnsafeResolver_GuardsWithPresent()
    {
        // Positive Eq is composed as: present AND predicate
        // Even if the predicate throws on missing/null, the guard prevents evaluation
        var resolver = new UnsafePredicateResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.value", Op = FilterOperator.Eq, Value = JsonValue(42) };
        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        // Entity with missing key: should not throw, should return false
        var entityMissing = new ExtensibleEntity { Custom = new() };
        Action act = () => predicate(entityMissing);
        act.Should().NotThrow();
        predicate(entityMissing).Should().BeFalse();

        // Entity with null value: should not throw, should return false
        var entityNull = new ExtensibleEntity { Custom = { { "value", null } } };
        act = () => predicate(entityNull);
        act.Should().NotThrow();
        predicate(entityNull).Should().BeFalse();
    }

    [Fact]
    public void Compile_NegatedEqOnUnsafeResolver_GuardsWithNotPresent()
    {
        // Negated Eq is composed as: NOT present OR NOT predicate
        // Missing/null returns true without evaluating the unsafe predicate
        var resolver = new UnsafePredicateResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.value", Op = FilterOperator.Neq, Value = JsonValue(42) };
        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        // Entity with missing key: should not throw, should return true (NOT present)
        var entityMissing = new ExtensibleEntity { Custom = new() };
        Action act = () => predicate(entityMissing);
        act.Should().NotThrow();
        predicate(entityMissing).Should().BeTrue();

        // Entity with null value: should not throw, should return true
        var entityNull = new ExtensibleEntity { Custom = { { "value", null } } };
        act = () => predicate(entityNull);
        act.Should().NotThrow();
        predicate(entityNull).Should().BeTrue();
    }

    [Fact]
    public void Compile_NegatedContainsOnUnsafeResolver_GuardsWithNotPresent()
    {
        var resolver = new UnsafePredicateResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.text", Op = FilterOperator.NotContains, Value = JsonValue("hello") };
        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        // Should not throw on missing/null, should return true
        var entityMissing = new ExtensibleEntity { Custom = new() };
        predicate(entityMissing).Should().BeTrue();

        var entityNull = new ExtensibleEntity { Custom = { { "text", null } } };
        predicate(entityNull).Should().BeTrue();
    }

    [Fact]
    public void Compile_PositiveGtOnUnsafeResolver_GuardsWithPresent()
    {
        var resolver = new UnsafePredicateResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.level", Op = FilterOperator.Gt, Value = JsonValue(10) };
        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        // Should not throw on missing/null, should return false
        var entityMissing = new ExtensibleEntity { Custom = new() };
        predicate(entityMissing).Should().BeFalse();

        var entityNull = new ExtensibleEntity { Custom = { { "level", null } } };
        predicate(entityNull).Should().BeFalse();
    }

    [Fact]
    public void Compile_NegatedStringOpsOnDynamicField_FiltersCorrectly()
    {
        var resolver = new StringPredicateResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        // NotContains: NOT present OR NOT predicate
        var notContains = new ComparisonNode { Field = "custom.name", Op = FilterOperator.NotContains, Value = JsonValue("bob") };
        var notContainsPred = FilterCompiler.Compile<ExtensibleEntity>(notContains, map).Compile();

        var entityMissing = new ExtensibleEntity { Custom = new() };
        var entityNull = new ExtensibleEntity { Custom = { { "name", null } } };
        var entityMatching = new ExtensibleEntity { Custom = { { "name", "bob" } } };
        var entityNonMatching = new ExtensibleEntity { Custom = { { "name", "alice" } } };

        notContainsPred(entityMissing).Should().BeTrue();
        notContainsPred(entityNull).Should().BeTrue();
        notContainsPred(entityMatching).Should().BeFalse();
        notContainsPred(entityNonMatching).Should().BeTrue();

        // NotStartsWith
        var notStartsWith = new ComparisonNode { Field = "custom.code", Op = FilterOperator.NotStartsWith, Value = JsonValue("x") };
        var notStartsWithPred = FilterCompiler.Compile<ExtensibleEntity>(notStartsWith, map).Compile();

        notStartsWithPred(entityMissing).Should().BeTrue();
        notStartsWithPred(entityNull).Should().BeTrue();
        notStartsWithPred(new ExtensibleEntity { Custom = { { "code", "xyz" } } }).Should().BeFalse();
        notStartsWithPred(new ExtensibleEntity { Custom = { { "code", "abc" } } }).Should().BeTrue();

        // NotEndsWith
        var notEndsWith = new ComparisonNode { Field = "custom.suffix", Op = FilterOperator.NotEndsWith, Value = JsonValue("end") };
        var notEndsWithPred = FilterCompiler.Compile<ExtensibleEntity>(notEndsWith, map).Compile();

        notEndsWithPred(entityMissing).Should().BeTrue();
        notEndsWithPred(entityNull).Should().BeTrue();
        notEndsWithPred(new ExtensibleEntity { Custom = { { "suffix", "theend" } } }).Should().BeFalse();
        notEndsWithPred(new ExtensibleEntity { Custom = { { "suffix", "start" } } }).Should().BeTrue();
    }

    [Fact]
    public void Compile_GteAndLtePassthrough_OnDynamicField()
    {
        var resolver = new ComparisonPredicateResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var gte = new ComparisonNode { Field = "custom.score", Op = FilterOperator.Gte, Value = JsonValue(80) };
        var gtePred = FilterCompiler.Compile<ExtensibleEntity>(gte, map).Compile();

        gtePred(new ExtensibleEntity { Custom = { { "score", 80 } } }).Should().BeTrue();
        gtePred(new ExtensibleEntity { Custom = { { "score", 90 } } }).Should().BeTrue();
        gtePred(new ExtensibleEntity { Custom = { { "score", 70 } } }).Should().BeFalse();

        var lte = new ComparisonNode { Field = "custom.limit", Op = FilterOperator.Lte, Value = JsonValue(100) };
        var ltePred = FilterCompiler.Compile<ExtensibleEntity>(lte, map).Compile();

        ltePred(new ExtensibleEntity { Custom = { { "limit", 100 } } }).Should().BeTrue();
        ltePred(new ExtensibleEntity { Custom = { { "limit", 50 } } }).Should().BeTrue();
        ltePred(new ExtensibleEntity { Custom = { { "limit", 110 } } }).Should().BeFalse();
    }

    [Fact]
    public void Compile_DynamicInWithResolverInBuilder_CallsInBuilderNotEq()
    {
        var resolver = new InPredicateResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.items", Op = FilterOperator.In, Value = JsonArray(1, 2, 3) };
        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        predicate(new ExtensibleEntity { Custom = { { "items", 2 } } }).Should().BeTrue();
        predicate(new ExtensibleEntity { Custom = { { "items", 5 } } }).Should().BeFalse();

        // Verify the In builder was used (not Eq)
        resolver.InBuilderCalled.Should().BeTrue();
        resolver.EqBuilderCalled.Should().BeFalse();
    }

    [Fact]
    public void Compile_DynamicInWithoutInOrEqBuilder_ThrowsValidationException()
    {
        var resolver = new GtOnlyResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.value", Op = FilterOperator.In, Value = JsonArray(1, 2) };

        var ex = Assert.Throws<ValidationException>(() => FilterCompiler.Compile<ExtensibleEntity>(node, map));

        ex.Errors.Should().ContainKey("filter");
        ex.Errors["filter"][0].Should().Contain("not allowed");
    }

    [Fact]
    public void Compile_DynamicOpWithoutBuilder_ThrowsValidationException()
    {
        var resolver = new EqOnlyResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.value", Op = FilterOperator.Lt, Value = JsonValue(100) };

        var ex = Assert.Throws<ValidationException>(() => FilterCompiler.Compile<ExtensibleEntity>(node, map));

        ex.Errors.Should().ContainKey("filter");
        ex.Errors["filter"][0].Should().Contain("not allowed");
    }

    [Fact]
    public void Compile_DynamicGtWithNullValue_ThrowsValidationException()
    {
        var resolver = new ComparisonPredicateResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.value", Op = FilterOperator.Gt, Value = JsonNull() };

        var ex = Assert.Throws<ValidationException>(() => FilterCompiler.Compile<ExtensibleEntity>(node, map));

        ex.Errors.Should().ContainKey("filter");
    }

    [Fact]
    public void Compile_DynamicInWithEmptyArray_ThrowsValidationExceptionWithEmptyErrorCode()
    {
        var resolver = new InPredicateResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.items", Op = FilterOperator.In, Value = JsonArray() };

        var ex = Assert.Throws<ValidationException>(() => FilterCompiler.Compile<ExtensibleEntity>(node, map));

        ex.ErrorCode.Should().Be(FilterInValues.EmptyErrorCode);
    }

    [Fact]
    public void Compile_DynamicInWithNullElement_ThrowsValidationExceptionWithNullElementErrorCode()
    {
        var resolver = new InPredicateResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.items", Op = FilterOperator.In, Value = JsonArray(1, null, 3) };

        var ex = Assert.Throws<ValidationException>(() => FilterCompiler.Compile<ExtensibleEntity>(node, map));

        ex.ErrorCode.Should().Be(FilterInValues.NullElementErrorCode);
    }

    [Fact]
    public void Compile_NullReturningBuilder_ThrowsInvalidOperationException()
    {
        var resolver = new NullReturningResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "custom.value", Op = FilterOperator.Eq, Value = JsonValue(42) };

        var ex = Assert.Throws<InvalidOperationException>(() => FilterCompiler.Compile<ExtensibleEntity>(node, map));
    }

    [Fact]
    public void Compile_PresenceNodeOnUnknownDynamicKey_ThrowsValidationException()
    {
        var resolver = new SpecificKeyResolver("age");
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        var node = new PresenceNode { Field = "custom.unknown" };

        var ex = Assert.Throws<ValidationException>(() => FilterCompiler.Compile<ExtensibleEntity>(node, map));

        ex.Errors.Should().ContainKey("filter");
        ex.Errors["filter"][0].Should().Contain("Unknown");
    }

    [Fact]
    public void Compile_TwoNonOverlappingPrefixes_ResolvedByCorrectResolver()
    {
        var resolver1 = new PrefixTrackingResolver();
        var resolver2 = new PrefixTrackingResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver1);
        builder.DynamicFields("ext.", resolver2);
        var map = builder.BuildFieldMap();

        // Field under first prefix
        var node1 = new ComparisonNode { Field = "custom.field", Op = FilterOperator.Eq, Value = JsonValue(1) };
        var predicate1 = FilterCompiler.Compile<ExtensibleEntity>(node1, map).Compile();
        predicate1(new ExtensibleEntity { Custom = { { "field", 1 } } }).Should().BeTrue();

        // Field under second prefix
        var node2 = new ComparisonNode { Field = "ext.field", Op = FilterOperator.Eq, Value = JsonValue(2) };
        var predicate2 = FilterCompiler.Compile<ExtensibleEntity>(node2, map).Compile();
        predicate2(new ExtensibleEntity { Custom = { { "field", 2 } } }).Should().BeTrue();

        resolver1.ResolvedKeys.Should().Contain("field");
        resolver2.ResolvedKeys.Should().Contain("field");
    }

    [Fact]
    public void Compile_PrefixCaseInsensitive_RequestCasePreserved()
    {
        var resolver = new CaseTrackingResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();
        builder.DynamicFields("custom.", resolver);
        var map = builder.BuildFieldMap();

        // Request with different case
        var node = new ComparisonNode { Field = "CUSTOM.AGE", Op = FilterOperator.Eq, Value = JsonValue(25) };
        var predicate = FilterCompiler.Compile<ExtensibleEntity>(node, map).Compile();

        // Resolver should be asked for "AGE" (case preserved)
        resolver.RequestedKeys.Should().Contain("AGE");
    }

    [Fact]
    public void BuildFieldMap_OverlappingPrefixesInReverseDirection_ThrowsArgumentException()
    {
        var resolver1 = new PrefixTrackingResolver();
        var resolver2 = new PrefixTrackingResolver();
        var builder = new QueryConfigurationBuilder<ExtensibleEntity, ExtensibleEntity>();

        builder.DynamicFields("custom.x.", resolver1);

        // Try to add overlapping prefix in reverse direction
        var ex = Assert.Throws<ArgumentException>(() =>
            builder.DynamicFields("custom.", resolver2));

        ex.Message.Should().Contain("overlaps");
    }

    [Fact]
    public void Compile_InOnNullableIntField_WithNullMember_ReturnsFalse()
    {
        var builder = new QueryConfigurationBuilder<EntityWithNullableInt, EntityWithNullableInt>();
        builder.Field(d => d.Size).AllowFilter(FilterOperator.In);
        var map = builder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Size", Op = FilterOperator.In, Value = JsonArray(10, 20, 30) };
        var predicate = FilterCompiler.Compile<EntityWithNullableInt>(node, map).Compile();

        // Null member is not in any set
        predicate(new EntityWithNullableInt { Id = 1, Size = null }).Should().BeFalse();
        predicate(new EntityWithNullableInt { Id = 2, Size = 20 }).Should().BeTrue();
        predicate(new EntityWithNullableInt { Id = 3, Size = 50 }).Should().BeFalse();
    }

    // Helper resolvers

    private sealed class StringPredicateResolver : IDynamicFieldResolver<ExtensibleEntity>
    {
        public DynamicField<ExtensibleEntity>? Resolve(string key)
        {
            var present = (Expression<Func<ExtensibleEntity, bool>>)(e => e.Custom.ContainsKey(key) && e.Custom[key] != null);
            var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
            {
                { FilterOperator.Contains, value => e => e.Custom.ContainsKey(key) && e.Custom[key]!.ToString()!.Contains(value.GetString()!) },
                { FilterOperator.StartsWith, value => e => e.Custom.ContainsKey(key) && e.Custom[key]!.ToString()!.StartsWith(value.GetString()!) },
                { FilterOperator.EndsWith, value => e => e.Custom.ContainsKey(key) && e.Custom[key]!.ToString()!.EndsWith(value.GetString()!) }
            };
            return new DynamicField<ExtensibleEntity>(present, predicates);
        }
    }

    private sealed class ComparisonPredicateResolver : IDynamicFieldResolver<ExtensibleEntity>
    {
        public DynamicField<ExtensibleEntity>? Resolve(string key)
        {
            var present = (Expression<Func<ExtensibleEntity, bool>>)(e => e.Custom.ContainsKey(key) && e.Custom[key] != null);
            var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
            {
                { FilterOperator.Gt, value => e => e.Custom.ContainsKey(key) && e.Custom[key] != null && (int)e.Custom[key]! > value.GetInt32() },
                { FilterOperator.Gte, value => e => e.Custom.ContainsKey(key) && e.Custom[key] != null && (int)e.Custom[key]! >= value.GetInt32() },
                { FilterOperator.Lt, value => e => e.Custom.ContainsKey(key) && e.Custom[key] != null && (int)e.Custom[key]! < value.GetInt32() },
                { FilterOperator.Lte, value => e => e.Custom.ContainsKey(key) && e.Custom[key] != null && (int)e.Custom[key]! <= value.GetInt32() }
            };
            return new DynamicField<ExtensibleEntity>(present, predicates);
        }
    }

    private sealed class InPredicateResolver : IDynamicFieldResolver<ExtensibleEntity>
    {
        public bool InBuilderCalled { get; private set; }
        public bool EqBuilderCalled { get; private set; }

        public DynamicField<ExtensibleEntity>? Resolve(string key)
        {
            var present = (Expression<Func<ExtensibleEntity, bool>>)(e => e.Custom.ContainsKey(key) && e.Custom[key] != null);
            var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
            {
                {
                    FilterOperator.In,
                    value =>
                    {
                        InBuilderCalled = true;
                        var items = value.EnumerateArray().Select(el => el.GetInt32()).ToHashSet();
                        return (Expression<Func<ExtensibleEntity, bool>>)(
                            e => e.Custom.ContainsKey(key) && e.Custom[key] != null && items.Contains((int)e.Custom[key]!)
                        );
                    }
                },
                {
                    FilterOperator.Eq,
                    value =>
                    {
                        EqBuilderCalled = true;
                        return e => e.Custom.ContainsKey(key) && e.Custom[key] != null && (int)e.Custom[key]! == value.GetInt32();
                    }
                }
            };
            return new DynamicField<ExtensibleEntity>(present, predicates);
        }
    }

    private sealed class GtOnlyResolver : IDynamicFieldResolver<ExtensibleEntity>
    {
        public DynamicField<ExtensibleEntity>? Resolve(string key)
        {
            var present = (Expression<Func<ExtensibleEntity, bool>>)(e => e.Custom.ContainsKey(key) && e.Custom[key] != null);
            var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
            {
                { FilterOperator.Gt, value => e => e.Custom.ContainsKey(key) && e.Custom[key] != null && (int)e.Custom[key]! > value.GetInt32() }
            };
            return new DynamicField<ExtensibleEntity>(present, predicates);
        }
    }

    private sealed class EqOnlyResolver : IDynamicFieldResolver<ExtensibleEntity>
    {
        public DynamicField<ExtensibleEntity>? Resolve(string key)
        {
            var present = (Expression<Func<ExtensibleEntity, bool>>)(e => e.Custom.ContainsKey(key) && e.Custom[key] != null);
            var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
            {
                { FilterOperator.Eq, value => e => e.Custom.ContainsKey(key) && e.Custom[key] != null && (int)e.Custom[key]! == value.GetInt32() }
            };
            return new DynamicField<ExtensibleEntity>(present, predicates);
        }
    }

    private sealed class NullReturningResolver : IDynamicFieldResolver<ExtensibleEntity>
    {
        public DynamicField<ExtensibleEntity>? Resolve(string key)
        {
            var present = (Expression<Func<ExtensibleEntity, bool>>)(e => e.Custom.ContainsKey(key));
            Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>> nullBuilder = _ => null!;
            var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
            {
                { FilterOperator.Eq, nullBuilder }
            };
            return new DynamicField<ExtensibleEntity>(present, predicates);
        }
    }

    private sealed class SpecificKeyResolver : IDynamicFieldResolver<ExtensibleEntity>
    {
        private readonly string _knownKey;

        public SpecificKeyResolver(string knownKey) => _knownKey = knownKey;

        public DynamicField<ExtensibleEntity>? Resolve(string key)
        {
            if (!key.Equals(_knownKey, StringComparison.Ordinal))
                return null;

            var present = (Expression<Func<ExtensibleEntity, bool>>)(e => e.Custom.ContainsKey(key));
            var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>();
            return new DynamicField<ExtensibleEntity>(present, predicates);
        }
    }

    private sealed class PrefixTrackingResolver : IDynamicFieldResolver<ExtensibleEntity>
    {
        private readonly HashSet<string> _resolvedKeys = new();

        public IReadOnlySet<string> ResolvedKeys => _resolvedKeys;

        public DynamicField<ExtensibleEntity>? Resolve(string key)
        {
            _resolvedKeys.Add(key);
            var present = (Expression<Func<ExtensibleEntity, bool>>)(e => e.Custom.ContainsKey(key));
            var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
            {
                { FilterOperator.Eq, value => e => e.Custom.ContainsKey(key) && e.Custom[key] != null && (int)e.Custom[key]! == value.GetInt32() }
            };
            return new DynamicField<ExtensibleEntity>(present, predicates);
        }
    }

    private sealed class CaseTrackingResolver : IDynamicFieldResolver<ExtensibleEntity>
    {
        private readonly HashSet<string> _requestedKeys = new();

        public IReadOnlySet<string> RequestedKeys => _requestedKeys;

        public DynamicField<ExtensibleEntity>? Resolve(string key)
        {
            _requestedKeys.Add(key);
            var present = (Expression<Func<ExtensibleEntity, bool>>)(e => e.Custom.ContainsKey(key.ToLower()));
            var predicates = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<ExtensibleEntity, bool>>>>
            {
                { FilterOperator.Eq, value => e => e.Custom.ContainsKey(key.ToLower()) && e.Custom[key.ToLower()] != null && (int)e.Custom[key.ToLower()]! == value.GetInt32() }
            };
            return new DynamicField<ExtensibleEntity>(present, predicates);
        }
    }
}
