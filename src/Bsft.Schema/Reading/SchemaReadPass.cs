using System.Text.Json;

namespace Bsft.Schema.Reading;

/// <summary>One reading of one schema: what is under what, and whether it may be used.</summary>
/// <remarks>
/// A per-call object rather than state on the reader, because the reader is a singleton and a node
/// budget shared between two concurrent reads would refuse whichever arrived second.
/// </remarks>
internal sealed class SchemaReadPass
{
    private readonly SchemaViolationLog _log;
    private readonly SchemaBudget _budget;
    private readonly SchemaKeywordCheck _keywords;
    private readonly SchemaConstraintReader _constraints;

    /// <summary>Initializes a new instance of the <see cref="SchemaReadPass"/> class.</summary>
    /// <param name="log">Where a refusal goes.</param>
    /// <param name="budget">How much of the schema this reading may walk.</param>
    /// <param name="keywords">Refuses a keyword outside the subset.</param>
    /// <param name="constraints">Reads the keywords that bound one value.</param>
    public SchemaReadPass(
        SchemaViolationLog log,
        SchemaBudget budget,
        SchemaKeywordCheck keywords,
        SchemaConstraintReader constraints)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));
        _keywords = keywords ?? throw new ArgumentNullException(nameof(keywords));
        _constraints = constraints ?? throw new ArgumentNullException(nameof(constraints));
    }

    /// <summary>Reads one node and everything under it.</summary>
    /// <param name="element">The submitted node.</param>
    /// <param name="path">Where it sits.</param>
    /// <param name="depth">How deep it sits, the root being one.</param>
    /// <returns>The node, or null when it or anything under it was refused.</returns>
    public SchemaNode? ReadNode(JsonElement element, string path, int depth)
    {
        if (!_budget.IsWithinBudget(path, depth))
            return null;

        if (element.ValueKind is not JsonValueKind.Object)
        {
            // The root reaches this too, and keeps its own code: a submission that is not an object at
            // all is a different repair from one property written as a value rather than as a schema.
            var code = path.Length == 0 ? SchemaCodes.SchemaRootNotAnObject : SchemaCodes.SchemaNodeNotAnObject;

            _log.Refuse(path, code, Describe(path) + " must be a JSON object.");
            return null;
        }

        var kind = ReadKind(element, path);

        if (kind is null)
            return null;

        _keywords.RefuseUnknown(element, path, kind.Value);

        return Build(element, path, depth, kind.Value);
    }

    private SchemaValueKind? ReadKind(JsonElement element, string path)
    {
        if (!element.TryGetProperty("type", out var type) || type.ValueKind is not JsonValueKind.String)
        {
            _log.Refuse(path, SchemaCodes.SchemaTypeNotSupported, Describe(path) + " must declare a single string \"type\".");
            return null;
        }

        var name = type.GetString();

        var kind = name switch
        {
            "string" => SchemaValueKind.String,
            "integer" => SchemaValueKind.Integer,
            "number" => SchemaValueKind.Number,
            "boolean" => SchemaValueKind.Boolean,
            "array" => SchemaValueKind.Array,
            "object" => SchemaValueKind.Object,
            _ => (SchemaValueKind?)null,
        };

        if (kind is null)
            _log.Refuse(path, SchemaCodes.SchemaTypeNotSupported, $"The type \"{name}\" is not one this platform supports.");

        return kind;
    }

    private SchemaNode? Build(JsonElement element, string path, int depth, SchemaValueKind kind)
    {
        var before = _log.Count;

        var node = new SchemaNode
        {
            Kind = kind,
            Format = _constraints.ReadFormat(element, path, kind),
            Pattern = _constraints.ReadPattern(element, path),
            AllowedValues = _constraints.ReadAllowedValues(element, path),
            Default = element.TryGetProperty("default", out var fallback) ? fallback.Clone() : null,
            MinLength = _constraints.ReadCount(element, path, "minLength"),
            MaxLength = _constraints.ReadCount(element, path, "maxLength"),
            Minimum = _constraints.ReadBound(element, path, "minimum"),
            Maximum = _constraints.ReadBound(element, path, "maximum"),
            MinItems = _constraints.ReadCount(element, path, "minItems"),
            MaxItems = _constraints.ReadCount(element, path, "maxItems"),
            Items = ReadItems(element, path, depth, kind),
            Properties = ReadProperties(element, path, depth, kind),
            Required = ReadRequired(element, path),
        };

        RefuseAdmittedExtras(element, path);
        RefuseUndeclaredRequired(node, path);

        return _log.Count == before ? node : null;
    }

    private SchemaNode? ReadItems(JsonElement element, string path, int depth, SchemaValueKind kind)
    {
        if (kind is not SchemaValueKind.Array)
            return null;

        if (!element.TryGetProperty("items", out var items))
        {
            _log.Refuse(path, SchemaCodes.SchemaKeywordMalformed, "An array must declare \"items\", the schema of one element.");
            return null;
        }

        return ReadNode(items, JsonPointer.Append(path, "items"), depth + 1);
    }

    private IReadOnlyDictionary<string, SchemaNode>? ReadProperties(
        JsonElement element, string path, int depth, SchemaValueKind kind)
    {
        if (kind is not SchemaValueKind.Object)
            return null;

        if (!element.TryGetProperty("properties", out var properties) || properties.ValueKind is not JsonValueKind.Object)
        {
            _log.Refuse(path, SchemaCodes.SchemaKeywordMalformed, "An object must declare \"properties\".");
            return null;
        }

        var declared = new Dictionary<string, SchemaNode>(StringComparer.Ordinal);

        foreach (var property in properties.EnumerateObject())
        {
            if (declared.Count == _budget.MaxProperties)
            {
                _budget.RefuseTooManyProperties(path);
                break;
            }

            var child = ReadNode(property.Value, JsonPointer.Append(path, property.Name), depth + 1);

            if (child is not null)
                declared[property.Name] = child;
        }

        return declared;
    }

    private IReadOnlyList<string> ReadRequired(JsonElement element, string path)
    {
        if (!element.TryGetProperty("required", out var required))
            return [];

        // Checked while being read rather than in a pass of its own, so the array is walked once.
        if (TryReadNames(required, out var names))
            return names;

        _log.Refuse(
            JsonPointer.Append(path, "required"),
            SchemaCodes.SchemaKeywordMalformed,
            "\"required\" must be an array of property names.");

        return [];
    }

    private static bool TryReadNames(JsonElement required, out IReadOnlyList<string> names)
    {
        names = [];

        if (required.ValueKind is not JsonValueKind.Array)
            return false;

        var read = new List<string>();

        foreach (var name in required.EnumerateArray())
        {
            if (name.ValueKind is not JsonValueKind.String)
                return false;

            read.Add(name.GetString()!);
        }

        names = read;

        return true;
    }

    private void RefuseAdmittedExtras(JsonElement element, string path)
    {
        if (!element.TryGetProperty("additionalProperties", out var extras) || extras.ValueKind is JsonValueKind.False)
            return;

        _log.Refuse(
            JsonPointer.Append(path, "additionalProperties"),
            SchemaCodes.SchemaKeywordNotSupported,
            "\"additionalProperties\" may only be false: an object with room in it for anything cannot be read back.");
    }

    private void RefuseUndeclaredRequired(SchemaNode node, string path)
    {
        foreach (var name in node.Required)
        {
            if (node.Properties?.ContainsKey(name) != true)
            {
                _log.Refuse(
                    JsonPointer.Append(path, "required"),
                    SchemaCodes.SchemaRequiredUnknown,
                    $"\"{name}\" is required but is not one of the declared properties.");
            }
        }
    }

    private static string Describe(string path) => path.Length == 0 ? "The schema" : $"The schema at \"{path}\"";
}
