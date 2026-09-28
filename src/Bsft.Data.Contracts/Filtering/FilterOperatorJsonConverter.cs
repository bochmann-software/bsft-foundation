using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bsft.Data.Contracts.Filtering;

/// <summary>
/// Custom JSON converter for <see cref="FilterOperator"/> that maps to the wire format used in filter expressions.
/// </summary>
public sealed class FilterOperatorJsonConverter : JsonConverter<FilterOperator>
{
    private static readonly Dictionary<string, FilterOperator> _nameToValue = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eq"] = FilterOperator.Eq,
        ["neq"] = FilterOperator.Neq,
        ["gt"] = FilterOperator.Gt,
        ["gte"] = FilterOperator.Gte,
        ["lt"] = FilterOperator.Lt,
        ["lte"] = FilterOperator.Lte,
        ["contains"] = FilterOperator.Contains,
        ["!contains"] = FilterOperator.NotContains,
        ["startsWith"] = FilterOperator.StartsWith,
        ["!startsWith"] = FilterOperator.NotStartsWith,
        ["endsWith"] = FilterOperator.EndsWith,
        ["!endsWith"] = FilterOperator.NotEndsWith,
        ["pr"] = FilterOperator.Present
    };

    private static readonly Dictionary<FilterOperator, string> _valueToName = new()
    {
        [FilterOperator.Eq] = "eq",
        [FilterOperator.Neq] = "neq",
        [FilterOperator.Gt] = "gt",
        [FilterOperator.Gte] = "gte",
        [FilterOperator.Lt] = "lt",
        [FilterOperator.Lte] = "lte",
        [FilterOperator.Contains] = "contains",
        [FilterOperator.NotContains] = "!contains",
        [FilterOperator.StartsWith] = "startsWith",
        [FilterOperator.NotStartsWith] = "!startsWith",
        [FilterOperator.EndsWith] = "endsWith",
        [FilterOperator.NotEndsWith] = "!endsWith",
        [FilterOperator.Present] = "pr"
    };

    /// <inheritdoc />
    public override FilterOperator Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (value is not null && _nameToValue.TryGetValue(value, out var op))
            return op;

        throw new JsonException($"Unknown filter operator: '{value}'");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, FilterOperator value, JsonSerializerOptions options)
    {
        if (_valueToName.TryGetValue(value, out var name))
        {
            writer.WriteStringValue(name);
            return;
        }

        writer.WriteStringValue(value.ToString());
    }
}
