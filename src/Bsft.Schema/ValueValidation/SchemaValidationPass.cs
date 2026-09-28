using System.Globalization;
using System.Text.Json;

namespace Bsft.Schema.ValueValidation;

/// <summary>One checking of one document: which value sits where, and whether it belongs there.</summary>
internal sealed class SchemaValidationPass
{
    private readonly BsftSchemaOptions _bounds;
    private readonly SchemaViolationLog _log;
    private readonly SchemaValueCheck _values;

    /// <summary>Initializes a new instance of the <see cref="SchemaValidationPass"/> class.</summary>
    /// <param name="bounds">The bounds this checking is held to.</param>
    /// <param name="log">Where a refusal goes.</param>
    /// <param name="values">Checks one leaf against what its node admits.</param>
    public SchemaValidationPass(BsftSchemaOptions bounds, SchemaViolationLog log, SchemaValueCheck values)
    {
        _bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    /// <summary>Checks one value against one node.</summary>
    /// <param name="node">What the schema says the value is.</param>
    /// <param name="value">The value.</param>
    /// <param name="path">Where it sits.</param>
    /// <param name="depth">How deep it sits, the document itself being one.</param>
    public void Check(SchemaNode node, JsonElement value, string path, int depth)
    {
        if (depth > _bounds.MaxDepth)
        {
            _log.Refuse(path, SchemaCodes.ValueTooLarge, $"This value nests deeper than {_bounds.MaxDepth} levels.");
            return;
        }

        if (!IsOfKind(node.Kind, value))
        {
            _log.Refuse(path, SchemaCodes.ValueTypeMismatch, $"{SchemaValueCheck.Describe(path)} must be {Name(node.Kind)}.");
            return;
        }

        _values.CheckChoices(node, value, path);

        CheckKind(node, value, path, depth);
    }

    private void CheckKind(SchemaNode node, JsonElement value, string path, int depth)
    {
        switch (node.Kind)
        {
            case SchemaValueKind.String:
                _values.CheckString(node, value, path);
                break;
            case SchemaValueKind.Integer:
            case SchemaValueKind.Number:
                _values.CheckNumber(node, value, path);
                break;
            case SchemaValueKind.Array:
                CheckArray(node, value, path, depth);
                break;
            case SchemaValueKind.Object:
                CheckObject(node, value, path, depth);
                break;
            default:
                // Boolean, which carries nothing beyond being true or false.
                break;
        }
    }

    // Object is the default arm rather than a case of its own: every kind is named here, and an arm the
    // compiler cannot prove unreachable is an arm no test can reach either.
    private static bool IsOfKind(SchemaValueKind kind, JsonElement value) => kind switch
    {
        SchemaValueKind.String => value.ValueKind is JsonValueKind.String,
        SchemaValueKind.Integer => value.ValueKind is JsonValueKind.Number && value.TryGetInt64(out _),
        SchemaValueKind.Number => value.ValueKind is JsonValueKind.Number,
        SchemaValueKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        SchemaValueKind.Array => value.ValueKind is JsonValueKind.Array,
        _ => value.ValueKind is JsonValueKind.Object,
    };

    private static string Name(SchemaValueKind kind) => kind switch
    {
        SchemaValueKind.String => "text",
        SchemaValueKind.Integer => "a whole number",
        SchemaValueKind.Number => "a number",
        SchemaValueKind.Boolean => "true or false",
        SchemaValueKind.Array => "a list",
        _ => "a set of named fields",
    };

    private void CheckArray(SchemaNode node, JsonElement value, string path, int depth)
    {
        var count = value.GetArrayLength();

        if (count > _bounds.MaxArrayItems)
        {
            _log.Refuse(
                path,
                SchemaCodes.ValueTooLarge,
                $"{SchemaValueCheck.Describe(path)} may hold at most {_bounds.MaxArrayItems} entries.");

            return;
        }

        CheckItemCount(node, count, path);

        var index = 0;

        foreach (var item in value.EnumerateArray())
        {
            Check(node.Items!, item, JsonPointer.Append(path, index.ToString(CultureInfo.InvariantCulture)), depth + 1);
            index++;
        }
    }

    private void CheckItemCount(SchemaNode node, int count, string path)
    {
        if (node.MinItems is { } min && count < min)
        {
            _log.Refuse(
                path,
                SchemaCodes.ValueItemCountOutOfRange,
                $"{SchemaValueCheck.Describe(path)} must hold at least {min} entries.");
        }

        if (node.MaxItems is { } max && count > max)
        {
            _log.Refuse(
                path,
                SchemaCodes.ValueItemCountOutOfRange,
                $"{SchemaValueCheck.Describe(path)} must hold at most {max} entries.");
        }
    }

    private void CheckObject(SchemaNode node, JsonElement value, string path, int depth)
    {
        var declared = node.Properties!;
        var present = 0;

        foreach (var property in value.EnumerateObject())
        {
            if (++present > _bounds.MaxProperties)
            {
                _log.Refuse(
                    path,
                    SchemaCodes.ValueTooLarge,
                    $"{SchemaValueCheck.Describe(path)} may carry at most {_bounds.MaxProperties} fields.");

                return;
            }

            CheckProperty(declared, property, path, depth);
        }

        CheckRequired(node, value, path);
    }

    private void CheckProperty(
        IReadOnlyDictionary<string, SchemaNode> declared, JsonProperty property, string path, int depth)
    {
        var at = JsonPointer.Append(path, property.Name);

        if (declared.TryGetValue(property.Name, out var child))
            Check(child, property.Value, at, depth + 1);
        else
            _log.Refuse(at, SchemaCodes.ValueUnknownProperty, $"\"{property.Name}\" is not a field this application accepts.");
    }

    private void CheckRequired(SchemaNode node, JsonElement value, string path)
    {
        foreach (var name in node.Required)
        {
            if (!value.TryGetProperty(name, out _))
                _log.Refuse(JsonPointer.Append(path, name), SchemaCodes.ValueRequired, $"\"{name}\" is required.");
        }
    }
}
