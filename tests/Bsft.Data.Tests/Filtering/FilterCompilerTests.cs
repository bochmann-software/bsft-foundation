using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Bsft.Data.Contracts.Filtering;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

public class FilterCompilerTests
{
    private readonly QueryFieldMap<TestEntity> _fieldMap;

    public FilterCompilerTests()
    {
        var config = new TestQueryConfiguration();
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        config.Configure(builder);
        _fieldMap = builder.BuildFieldMap();
    }

    private static JsonElement JsonValue(object value)
    {
        return JsonSerializer.SerializeToElement(value);
    }

    private static JsonElement JsonNull()
    {
        return JsonSerializer.SerializeToElement<object?>(null);
    }

    [Fact]
    public void Compile_EqOperator_FiltersCorrectly()
    {
        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonValue("Alice") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Bob" }).Should().BeFalse();
    }

    [Fact]
    public void Compile_EqOperator_IsCaseInsensitive()
    {
        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonValue("alice") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "ALICE" }).Should().BeTrue();
    }

    [Fact]
    public void Compile_ContainsOperator_FiltersCorrectly()
    {
        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Contains, Value = JsonValue("li") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Bob" }).Should().BeFalse();
    }

    [Fact]
    public void Compile_ContainsOperator_WithNullMember_DoesNotThrowAndReturnsFalse()
    {
        // Regression: a null string property must not throw NullReferenceException during in-memory
        // evaluation (null is treated as empty, so it contains nothing).
        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Contains, Value = JsonValue("li") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        Action act = () => predicate(new TestEntity { Name = null! });
        act.Should().NotThrow();
        predicate(new TestEntity { Name = null! }).Should().BeFalse();
    }

    [Fact]
    public void Compile_EqOperator_WithNullMember_DoesNotThrowAndReturnsFalse()
    {
        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonValue("alice") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        predicate(new TestEntity { Name = null! }).Should().BeFalse();
    }

    [Fact]
    public void Compile_NotContainsOperator_FiltersCorrectly()
    {
        // Exercises FilterCompiler.BuildStringComparison line 67: NotContains.
        var customBuilder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        customBuilder.Field(d => d.Name).AllowFilter();
        var customMap = customBuilder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.NotContains, Value = JsonValue("li") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, customMap).Compile();

        predicate(new TestEntity { Name = "Bob" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Alice" }).Should().BeFalse();
    }

    [Fact]
    public void Compile_StartsWithOperator_FiltersCorrectly()
    {
        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.StartsWith, Value = JsonValue("Al") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Bob" }).Should().BeFalse();
    }

    [Fact]
    public void Compile_GtOperator_FiltersCorrectly()
    {
        var node = new ComparisonNode { Field = "Age", Op = FilterOperator.Gt, Value = JsonValue(25) };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        predicate(new TestEntity { Age = 30 }).Should().BeTrue();
        predicate(new TestEntity { Age = 25 }).Should().BeFalse();
        predicate(new TestEntity { Age = 20 }).Should().BeFalse();
    }

    [Fact]
    public void Compile_GteOperator_FiltersCorrectly()
    {
        var node = new ComparisonNode { Field = "Age", Op = FilterOperator.Gte, Value = JsonValue(25) };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        predicate(new TestEntity { Age = 25 }).Should().BeTrue();
        predicate(new TestEntity { Age = 30 }).Should().BeTrue();
        predicate(new TestEntity { Age = 20 }).Should().BeFalse();
    }

    [Fact]
    public void Compile_LtOperator_FiltersCorrectly()
    {
        var node = new ComparisonNode { Field = "Age", Op = FilterOperator.Lt, Value = JsonValue(25) };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        predicate(new TestEntity { Age = 20 }).Should().BeTrue();
        predicate(new TestEntity { Age = 25 }).Should().BeFalse();
    }

    [Fact]
    public void Compile_LteOperator_FiltersCorrectly()
    {
        var node = new ComparisonNode { Field = "Age", Op = FilterOperator.Lte, Value = JsonValue(25) };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        predicate(new TestEntity { Age = 25 }).Should().BeTrue();
        predicate(new TestEntity { Age = 20 }).Should().BeTrue();
        predicate(new TestEntity { Age = 30 }).Should().BeFalse();
    }

    [Fact]
    public void Compile_AndNode_CombinesFilters()
    {
        var node = new AndNode
        {
            And =
            [
                new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonValue("Alice") },
                new ComparisonNode { Field = "Age", Op = FilterOperator.Gt, Value = JsonValue(20) }
            ]
        };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        predicate(new TestEntity { Name = "Alice", Age = 25 }).Should().BeTrue();
        predicate(new TestEntity { Name = "Alice", Age = 15 }).Should().BeFalse();
        predicate(new TestEntity { Name = "Bob", Age = 25 }).Should().BeFalse();
    }

    [Fact]
    public void Compile_OrNode_CombinesFilters()
    {
        var node = new OrNode
        {
            Or =
            [
                new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonValue("Alice") },
                new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonValue("Bob") }
            ]
        };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Bob" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Charlie" }).Should().BeFalse();
    }

    [Fact]
    public void Compile_StripConvert_NonConvertExpression_CompilesWithoutThrowing()
    {
        // Exercises FilterCompiler.StripConvert line 133:
        // When entity expression body is NOT a UnaryExpression{Convert}, returns expression unchanged.
        // Use an entity with an object? property AND MapsTo so the expression body has type object?
        // and the C# compiler does NOT insert a Convert node.
        // TryBuildDefaultEntityExpression always inserts Convert, so MapsTo must be used explicitly.
        var builder = new QueryConfigurationBuilder<ObjectPropEntity, ObjectPropDto>();
        builder.Field(d => d.ObjProp)
            .MapsTo(e => e.ObjProp)  // object? -> object?: no Convert inserted by C# compiler
            .AllowFilter(FilterOperator.Eq);
        var fieldMap = builder.BuildFieldMap();

        var node = new ComparisonNode
        {
            Field = "ObjProp",
            Op = FilterOperator.Eq,
            Value = System.Text.Json.JsonSerializer.SerializeToElement("hello")
        };

        // The predicate should compile without throwing; StripConvert line 133 (return expression) is hit
        // because the MapsTo lambda body for object? property has no Convert wrapper.
        var act = () => FilterCompiler.Compile<ObjectPropEntity>(node, fieldMap);

        act.Should().NotThrow();
    }

    [Fact]
    public void Compile_UnknownNodeType_ThrowsInvalidOperationException()
    {
        // Exercises FilterCompiler.BuildExpression line 40: unknown node type -> throws.
        // Create a custom FilterNode subclass not handled by the switch.
        var node = new CustomFilterNode();

        var act = () => FilterCompiler.Compile<TestEntity>(node, _fieldMap);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown filter node type*");
    }

    [Fact]
    public void Compile_NullFieldMap_ThrowsArgumentNullException()
    {
        // Exercises FilterCompiler.Compile line 25-26: null fieldMap guard.
        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonValue("x") };

        var act = () => FilterCompiler.Compile<TestEntity>(node, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("fieldMap");
    }

    [Fact]
    public void Compile_NullNode_ThrowsArgumentNullException()
    {
        var act = () => FilterCompiler.Compile<TestEntity>(null!, _fieldMap);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Compile_UnknownField_ThrowsValidationException()
    {
        // A field the client named that is not on the whitelist is a bad request, not an internal
        // fault: compiling it surfaces the framework's ValidationException (mapped to 400), never an
        // InvalidOperationException that would become a 500.
        var node = new ComparisonNode { Field = "NonExistent", Op = FilterOperator.Eq, Value = JsonValue("x") };

        var act = () => FilterCompiler.Compile<TestEntity>(node, _fieldMap);

        act.Should().Throw<Bsft.Core.ValidationException>()
            .Which.Errors.Should().ContainKey("filter")
            .WhoseValue.Should().ContainMatch("*Unknown filter field*");
    }

    [Fact]
    public void Compile_DisallowedOperator_ThrowsValidationException()
    {
        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Gt, Value = JsonValue("x") };

        var act = () => FilterCompiler.Compile<TestEntity>(node, _fieldMap);

        act.Should().Throw<Bsft.Core.ValidationException>()
            .Which.Errors.Should().ContainKey("filter")
            .WhoseValue.Should().ContainMatch("*not allowed*");
    }

    [Fact]
    public void Compile_EmptyAndNode_AlwaysReturnsTrue()
    {
        // Exercises FilterCompiler.BuildAnd line 100: node.And.Count == 0 -> return Expression.Constant(true).
        var node = new AndNode { And = [] };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        // An empty AND is always true (identity for AND)
        predicate(new TestEntity { Name = "anything" }).Should().BeTrue();
    }

    [Fact]
    public void Compile_EmptyOrNode_AlwaysReturnsFalse()
    {
        // Exercises FilterCompiler.BuildOr line 109: node.Or.Count == 0 -> return Expression.Constant(false).
        var node = new OrNode { Or = [] };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        // An empty OR is always false (identity for OR)
        predicate(new TestEntity { Name = "anything" }).Should().BeFalse();
    }

    [Fact]
    public void Compile_NeqOperator_FiltersCorrectly()
    {
        // Exercises FilterCompiler.BuildValueComparison line 88: Neq -> NotEqual.
        // Use a custom fieldMap that allows Neq on Name (string field).
        var customBuilder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        customBuilder.Field(d => d.Name).AllowFilter(); // allows all
        var customMap = customBuilder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Neq, Value = JsonValue("Alice") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, customMap).Compile();

        predicate(new TestEntity { Name = "Bob" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Alice" }).Should().BeFalse();
    }

    [Fact]
    public void Compile_NotStartsWithOperator_FiltersCorrectly()
    {
        // Exercises FilterCompiler.BuildStringComparison line 69: NotStartsWith.
        var customBuilder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        customBuilder.Field(d => d.Name).AllowFilter();
        var customMap = customBuilder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.NotStartsWith, Value = JsonValue("Al") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, customMap).Compile();

        predicate(new TestEntity { Name = "Bob" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Alice" }).Should().BeFalse();
    }

    [Fact]
    public void Compile_EndsWithOperator_FiltersCorrectly()
    {
        // Exercises FilterCompiler.BuildStringComparison line 70: EndsWith.
        var customBuilder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        customBuilder.Field(d => d.Name).AllowFilter();
        var customMap = customBuilder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.EndsWith, Value = JsonValue("ice") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, customMap).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Bob" }).Should().BeFalse();
    }

    [Fact]
    public void Compile_NotEndsWithOperator_FiltersCorrectly()
    {
        // Exercises FilterCompiler.BuildStringComparison line 71: NotEndsWith.
        var customBuilder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        customBuilder.Field(d => d.Name).AllowFilter();
        var customMap = customBuilder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.NotEndsWith, Value = JsonValue("ice") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, customMap).Compile();

        predicate(new TestEntity { Name = "Bob" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Alice" }).Should().BeFalse();
    }

    [Fact]
    public void Compile_UnsupportedValueOperator_ThrowsInvalidOperationException()
    {
        // Exercises FilterCompiler.BuildValueComparison line 93: unsupported operator -> throws.
        // Use a string operator (Contains etc.) on a non-string field (Age is int).
        // The Resolve step allows it if the operator is in allowedOperators; we need to bypass that.
        // Since TestQueryConfiguration allows Eq/Gt/Lt/Gte/Lte on Age,
        // we need an operator not in IsStringOperation and not in BuildValueComparison's switch.
        // The only way is a cast to an unsupported operator.
        // Actually, the simplest way: create a custom fieldMap that allows any operator on Age,
        // then use an operator that is a string operation (Contains) on the Age field.
        // The Contains operator is handled by IsStringOperation check -> goes to BuildStringComparison.
        // No, we need an operator that:
        //   1. IsStringOperation returns false (so it goes to BuildValueComparison)
        //   2. BuildValueComparison's switch doesn't handle it
        // The switch handles Eq, Neq, Gt, Gte, Lt, Lte. All of TestQueryConfiguration's allowed non-string ops.
        // We need an operator like (FilterOperator)999 that passes Resolve and is not string.

        // Create a custom fieldMap with an unrestricted field (empty allowedOperators)
        var customBuilder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        customBuilder.Field(d => d.Age).AllowFilter(); // empty allowedOperators -> allows all
        var customMap = customBuilder.BuildFieldMap();

        // Cast an integer to a FilterOperator not handled by BuildValueComparison
        // "EndsWith" is a string operation -> would go to BuildStringComparison -> throw there
        // We need one that is NOT a string operation.
        // (FilterOperator)999 is not a string operation and not in BuildValueComparison's switch.
        var unknownOp = (FilterOperator)999;
        var node = new ComparisonNode { Field = "Age", Op = unknownOp, Value = JsonValue(5) };

        var act = () => FilterCompiler.Compile<TestEntity>(node, customMap);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unsupported value operator*");
    }

    [Fact]
    public void BuildStringComparison_UnsupportedOperator_ThrowsInvalidOperationException()
    {
        // Exercises FilterCompiler.BuildStringComparison line 72: _ => throw new InvalidOperationException.
        // BuildStringComparison is private static and only reachable via IsStringOperation gating.
        // We call it directly via reflection to cover the default arm that IsStringOperation prevents.
        var buildStringComparison = typeof(FilterCompiler)
            .GetMethod("BuildStringComparison", BindingFlags.NonPublic | BindingFlags.Static)!;

        // Create dummy expressions for the member and value parameters (string type).
        var memberExpr = Expression.Constant("test");
        var valueExpr = Expression.Constant("pattern");

        // (FilterOperator)999 is not in the switch -> hits the _ => throw arm (line 72).
        var unsupportedOp = (FilterOperator)999;

        var act = () =>
        {
            try
            {
                buildStringComparison.Invoke(null, [unsupportedOp, memberExpr, valueExpr, FilterStringMatch.LowerInvariant]);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is InvalidOperationException)
            {
                throw tie.InnerException;
            }
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unsupported string operator*");
    }

    [Fact]
    public void Compile_PresentOperator_OnReferenceField_TrueWhenNotNull()
    {
        // pr (present) on a string/reference field: present exactly when the value is not null.
        var map = BuildPresenceMap();
        var node = new PresenceNode { Field = "Name" };

        var predicate = FilterCompiler.Compile<PresenceEntity>(node, map).Compile();

        predicate(new PresenceEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new PresenceEntity { Name = null }).Should().BeFalse();
    }

    [Fact]
    public void Compile_PresentOperator_OnNullableValueField_TrueWhenHasValue()
    {
        // pr on a Nullable<T> value field: present exactly when it has a value.
        var map = BuildPresenceMap();
        var node = new PresenceNode { Field = "OptionalAge" };

        var predicate = FilterCompiler.Compile<PresenceEntity>(node, map).Compile();

        predicate(new PresenceEntity { OptionalAge = 30 }).Should().BeTrue();
        predicate(new PresenceEntity { OptionalAge = null }).Should().BeFalse();
    }

    [Fact]
    public void Compile_PresentOperator_OnNonNullableValueField_AlwaysTrue()
    {
        // pr on a non-nullable value field: the value is always present, so the predicate is a constant
        // truth (never a null-check, which would not compile for an int).
        var map = BuildPresenceMap();
        var node = new PresenceNode { Field = "RequiredAge" };

        var predicate = FilterCompiler.Compile<PresenceEntity>(node, map).Compile();

        predicate(new PresenceEntity { RequiredAge = 0 }).Should().BeTrue();
        predicate(new PresenceEntity { RequiredAge = 42 }).Should().BeTrue();
    }

    [Fact]
    public void Compile_NullValue_OnNullableValueField_ComparesAgainstNull()
    {
        // A client may ask for the rows whose optional age is unset. Read as the unwrapped int, the
        // JSON null would throw out of System.Text.Json and reach the client as a 500.
        var builder = new QueryConfigurationBuilder<PresenceEntity, PresenceDto>();
        builder.Field(d => d.OptionalAge).AllowFilter(FilterOperator.Eq);
        var node = new ComparisonNode { Field = "OptionalAge", Op = FilterOperator.Eq, Value = JsonNull() };

        var predicate = FilterCompiler.Compile<PresenceEntity>(node, builder.BuildFieldMap()).Compile();

        predicate(new PresenceEntity { OptionalAge = null }).Should().BeTrue();
        predicate(new PresenceEntity { OptionalAge = 30 }).Should().BeFalse();
    }

    [Fact]
    public void Compile_FieldMappedToAnExpressionWithALambdaOfItsOwn_KeepsThatLambdasParameter()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Age).MapsTo(e => e.Name.Count(letter => letter == 'a')).AllowFilter(FilterOperator.Eq);
        var node = new ComparisonNode { Field = "Age", Op = FilterOperator.Eq, Value = JsonValue(2) };

        var predicate = FilterCompiler.Compile<TestEntity>(node, builder.BuildFieldMap()).Compile();

        predicate(new TestEntity { Name = "banana" }).Should().BeFalse();
        predicate(new TestEntity { Name = "aha" }).Should().BeTrue();
    }

    [Theory]
    [InlineData(FilterOperator.Eq, 30, true)]
    [InlineData(FilterOperator.Eq, 31, false)]
    [InlineData(FilterOperator.Gt, 29, true)]
    [InlineData(FilterOperator.Gt, 30, false)]
    public void Compile_ValueOnNullableValueField_ComparesTheValue(FilterOperator op, int value, bool matchesThirty)
    {
        // The value is read as the unwrapped int and compared against the Nullable<int> member; a row
        // whose optional age is unset matches no value comparison.
        var builder = new QueryConfigurationBuilder<PresenceEntity, PresenceDto>();
        builder.Field(d => d.OptionalAge).AllowFilter(op);
        var node = new ComparisonNode { Field = "OptionalAge", Op = op, Value = JsonValue(value) };

        var predicate = FilterCompiler.Compile<PresenceEntity>(node, builder.BuildFieldMap()).Compile();

        predicate(new PresenceEntity { OptionalAge = 30 }).Should().Be(matchesThirty);
        predicate(new PresenceEntity { OptionalAge = null }).Should().BeFalse();
    }

    [Fact]
    public void Compile_NullValue_OnNonNullableValueField_ThrowsValidationException()
    {
        var builder = new QueryConfigurationBuilder<PresenceEntity, PresenceDto>();
        builder.Field(d => d.RequiredAge).AllowFilter(FilterOperator.Eq);
        var node = new ComparisonNode { Field = "RequiredAge", Op = FilterOperator.Eq, Value = JsonNull() };

        var act = () => FilterCompiler.Compile<PresenceEntity>(node, builder.BuildFieldMap());

        act.Should().Throw<Bsft.Core.ValidationException>()
            .Which.Errors.Should().ContainKey("filter")
            .WhoseValue.Should().ContainMatch("*can never be null*");
    }

    [Fact]
    public void Compile_NullValue_OnStringField_MatchesOnlyTheRowsWithoutOne()
    {
        // Lowering both operands, which is what the default match mode does to a string comparison,
        // coalesces a null column to the empty string. A row that holds "" would then answer a request
        // for the rows that hold nothing.
        var builder = new QueryConfigurationBuilder<PresenceEntity, PresenceDto>();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq, FilterOperator.Neq);
        var isNull = new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonNull() };
        var isNotNull = new ComparisonNode { Field = "Name", Op = FilterOperator.Neq, Value = JsonNull() };

        var matchesNull = FilterCompiler.Compile<PresenceEntity>(isNull, builder.BuildFieldMap()).Compile();
        var matchesValue = FilterCompiler.Compile<PresenceEntity>(isNotNull, builder.BuildFieldMap()).Compile();

        matchesNull(new PresenceEntity { Name = null }).Should().BeTrue();
        matchesNull(new PresenceEntity { Name = string.Empty }).Should().BeFalse();
        matchesValue(new PresenceEntity { Name = string.Empty }).Should().BeTrue();
        matchesValue(new PresenceEntity { Name = null }).Should().BeFalse();
    }

    [Theory]
    [InlineData(FilterOperator.Contains)]
    [InlineData(FilterOperator.StartsWith)]
    [InlineData(FilterOperator.NotEndsWith)]
    public void Compile_NullValue_WithAStringOperator_ThrowsValidationException(FilterOperator op)
    {
        // Contains("") is true of every row, so letting this through would answer a malformed request
        // with the whole table rather than with a 400.
        var builder = new QueryConfigurationBuilder<PresenceEntity, PresenceDto>();
        builder.Field(d => d.Name).AllowFilter(op);
        var node = new ComparisonNode { Field = "Name", Op = op, Value = JsonNull() };

        var act = () => FilterCompiler.Compile<PresenceEntity>(node, builder.BuildFieldMap());

        act.Should().Throw<Bsft.Core.ValidationException>()
            .Which.Errors.Should().ContainKey("filter")
            .WhoseValue.Should().ContainMatch("*cannot be applied to a null value*");
    }

    [Fact]
    public void Compile_NullValue_WithAnOrderingOperator_ThrowsValidationException()
    {
        // A lifted ordering comparison against null is false for every row, so the request would come
        // back empty instead of refused.
        var builder = new QueryConfigurationBuilder<PresenceEntity, PresenceDto>();
        builder.Field(d => d.OptionalAge).AllowFilter(FilterOperator.Gt);
        var node = new ComparisonNode { Field = "OptionalAge", Op = FilterOperator.Gt, Value = JsonNull() };

        var act = () => FilterCompiler.Compile<PresenceEntity>(node, builder.BuildFieldMap());

        act.Should().Throw<Bsft.Core.ValidationException>()
            .Which.Errors.Should().ContainKey("filter")
            .WhoseValue.Should().ContainMatch("*cannot be applied to a null value*");
    }

    [Fact]
    public void Compile_PresentOperator_NotAllowedOnField_ThrowsValidationException()
    {
        // Presence runs through the same whitelist as every other operator: a field that does not
        // declare AllowFilter(Present) rejects a presence test as a 400.
        var builder = new QueryConfigurationBuilder<PresenceEntity, PresenceDto>();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq); // Present not allowed
        var map = builder.BuildFieldMap();
        var node = new PresenceNode { Field = "Name" };

        var act = () => FilterCompiler.Compile<PresenceEntity>(node, map);

        act.Should().Throw<Bsft.Core.ValidationException>()
            .Which.Errors.Should().ContainKey("filter")
            .WhoseValue.Should().ContainMatch("*not allowed*");
    }

    [Theory]
    [InlineData(FilterOperator.Contains)]
    [InlineData(FilterOperator.NotContains)]
    [InlineData(FilterOperator.StartsWith)]
    [InlineData(FilterOperator.NotStartsWith)]
    [InlineData(FilterOperator.EndsWith)]
    [InlineData(FilterOperator.NotEndsWith)]
    public void Compile_StringOperator_OnNonStringField_ThrowsValidationException(FilterOperator op)
    {
        // A string operator against a non-string field (Age is int) is a client mistake: it must be a
        // clean 400 (ValidationException keyed on `filter`), never a 500 from a failed expression build.
        var customBuilder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        customBuilder.Field(d => d.Age).AllowFilter(); // empty -> allows any operator through the whitelist
        var customMap = customBuilder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Age", Op = op, Value = JsonValue("5") };

        var act = () => FilterCompiler.Compile<TestEntity>(node, customMap);

        act.Should().Throw<Bsft.Core.ValidationException>()
            .Which.Errors.Should().ContainKey("filter")
            .WhoseValue.Should().ContainMatch("*only be applied to a string field*");
    }

    [Fact]
    public void Compile_ContainsOperator_TreatsLikeWildcardsAsLiterals()
    {
        // The value is data, not a LIKE pattern: `_` and `%` are matched literally, never as
        // single-character or multi-character wildcards. (In-memory string.Contains is literal by
        // construction; under EF Core the same instance methods translate to a parameterized,
        // wildcard-escaped LIKE, so the pattern a client sends cannot become an injected wildcard.)
        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Contains, Value = JsonValue("a_c") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap).Compile();

        predicate(new TestEntity { Name = "xa_cy" }).Should().BeTrue();  // literal underscore matches
        predicate(new TestEntity { Name = "abc" }).Should().BeFalse();   // underscore is not a wildcard
    }

    [Fact]
    public void Compile_PresentOperator_OnComparisonNode_ThrowsValidationException()
    {
        // A `comparison` carrying the unary `pr` operator is malformed — presence has its own node —
        // and must be a 400, never a 500 from the value-comparison switch's default arm.
        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Present, Value = JsonValue("x") };

        var act = () => FilterCompiler.Compile<TestEntity>(node, _fieldMap);

        act.Should().Throw<Bsft.Core.ValidationException>()
            .Which.Errors.Should().ContainKey("filter")
            .WhoseValue.Should().ContainMatch("*use a 'present' node*");
    }

    [Fact]
    public void Compile_CollationMode_EqOnString_IsCaseSensitiveInMemory()
    {
        // Collation mode leaves the column bare and defers case-folding to the DB collation, so an
        // in-memory ordinal comparison is case-sensitive: this proves the LOWER wrapping is gone.
        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Eq, Value = JsonValue("alice") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap, FilterStringMatch.Collation).Compile();

        predicate(new TestEntity { Name = "alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Alice" }).Should().BeFalse();
    }

    [Fact]
    public void Compile_CollationMode_ContainsOnString_IsCaseSensitiveInMemory()
    {
        // Collation mode on a string operator likewise skips the LOWER wrapping, so matching is ordinal
        // in memory and left to the column collation under a provider.
        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Contains, Value = JsonValue("li") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, _fieldMap, FilterStringMatch.Collation).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "ALICE" }).Should().BeFalse();
    }

    [Theory]
    [InlineData(FilterOperator.Contains, false)]
    [InlineData(FilterOperator.StartsWith, false)]
    [InlineData(FilterOperator.EndsWith, false)]
    [InlineData(FilterOperator.NotContains, true)]
    [InlineData(FilterOperator.NotStartsWith, true)]
    [InlineData(FilterOperator.NotEndsWith, true)]
    public void Compile_CollationMode_Contains_WithNullMember_DoesNotThrowAndReturnsFalse(FilterOperator op, bool expectedForNull)
    {
        // Regression: collation mode calls the string method on the bare column, so a null string
        // property must not throw NullReferenceException during in-memory / client-eval. Collation mode
        // matches SQL semantics — a null column does not match (positive ops => false, Not* => true).
        // This deliberately differs from LowerInvariant's vacuous-true on a null member with an empty pattern.
        var customBuilder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        customBuilder.Field(d => d.Name).AllowFilter();
        var customMap = customBuilder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Name", Op = op, Value = JsonValue("li") };

        var predicate = FilterCompiler.Compile<TestEntity>(node, customMap, FilterStringMatch.Collation).Compile();

        Action act = () => predicate(new TestEntity { Name = null! });
        act.Should().NotThrow();
        predicate(new TestEntity { Name = null! }).Should().Be(expectedForNull);
    }

    [Theory]
    [InlineData(FilterStringMatch.LowerInvariant)]
    [InlineData(FilterStringMatch.Collation)]
    public void Compile_StringOp_WithNullFilterValue_IsRefusedWhateverTheMatchMode(FilterStringMatch stringMatch)
    {
        // The refusal comes before the match mode is consulted, which is what makes it a property of
        // the request rather than of the mode: neither mode has a right answer for a null pattern.
        var customBuilder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        customBuilder.Field(d => d.Name).AllowFilter();
        var customMap = customBuilder.BuildFieldMap();

        var node = new ComparisonNode { Field = "Name", Op = FilterOperator.Contains, Value = JsonNull() };

        var act = () => FilterCompiler.Compile<TestEntity>(node, customMap, stringMatch);

        act.Should().Throw<Bsft.Core.ValidationException>()
            .Which.Errors.Should().ContainKey("filter")
            .WhoseValue.Should().ContainMatch("*cannot be applied to a null value*");
    }

    [Fact]
    public void StringMatch_ConfiguredMode_IsReadBackFromBuilder()
    {
        // The fluent opt-in stores the selected strategy so the executor can thread it into the compiler.
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();

        builder.GetStringMatch().Should().Be(FilterStringMatch.LowerInvariant);

        builder.StringMatch(FilterStringMatch.Collation);

        builder.GetStringMatch().Should().Be(FilterStringMatch.Collation);
    }

    private static QueryFieldMap<PresenceEntity> BuildPresenceMap()
    {
        var builder = new QueryConfigurationBuilder<PresenceEntity, PresenceDto>();
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Present);
        builder.Field(d => d.OptionalAge).AllowFilter(FilterOperator.Present);
        builder.Field(d => d.RequiredAge).AllowFilter(FilterOperator.Present);
        return builder.BuildFieldMap();
    }
}
