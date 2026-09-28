using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bsft.Schema.Reading;

/// <summary>Reads the keywords that bound one value, refusing any that is not of the shape it takes.</summary>
/// <remarks>
/// Separate from the node reader because these answer a different question from the same document: the
/// node reader is about structure - what is under what - and this is about what one leaf admits.
/// </remarks>
internal sealed class SchemaConstraintReader
{
    private readonly BsftSchemaOptions _bounds;
    private readonly SchemaViolationLog _log;

    /// <summary>Initializes a new instance of the <see cref="SchemaConstraintReader"/> class.</summary>
    /// <param name="bounds">The bounds a pattern is compiled within.</param>
    /// <param name="log">Where a refusal goes.</param>
    public SchemaConstraintReader(BsftSchemaOptions bounds, SchemaViolationLog log)
    {
        _bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Reads <c>format</c>, refusing one outside the list or one on a value that is not a string.</summary>
    /// <param name="element">The node.</param>
    /// <param name="path">Where it sits.</param>
    /// <param name="kind">What it declared itself to be.</param>
    /// <returns>The format, or null.</returns>
    public string? ReadFormat(JsonElement element, string path, SchemaValueKind kind)
    {
        if (!element.TryGetProperty("format", out var format))
            return null;

        if (format.ValueKind is not JsonValueKind.String)
        {
            _log.Refuse(JsonPointer.Append(path, "format"), SchemaCodes.SchemaKeywordMalformed, "\"format\" must be a string.");
            return null;
        }

        var name = format.GetString()!;

        if (kind is SchemaValueKind.String && IsKnownFormat(name))
            return name;

        _log.Refuse(
            JsonPointer.Append(path, "format"),
            SchemaCodes.SchemaFormatNotSupported,
            $"The format \"{name}\" is not one this platform supports on a string.");

        return null;
    }

    /// <summary>Reads and compiles <c>pattern</c>.</summary>
    /// <param name="element">The node.</param>
    /// <param name="path">Where it sits.</param>
    /// <returns>The compiled expression, or null.</returns>
    /// <remarks>
    /// Backtracking off is what removes the catastrophic case outright rather than budgeting for it.
    /// The cost is that backreferences and lookaround are refused, which is deliberate: the expression
    /// arrived with the application.
    /// </remarks>
    public Regex? ReadPattern(JsonElement element, string path)
    {
        if (!element.TryGetProperty("pattern", out var pattern))
            return null;

        if (pattern.ValueKind is not JsonValueKind.String)
        {
            _log.Refuse(JsonPointer.Append(path, "pattern"), SchemaCodes.SchemaKeywordMalformed, "\"pattern\" must be a string.");
            return null;
        }

        try
        {
            return new Regex(pattern.GetString()!, RegexOptions.NonBacktracking, _bounds.PatternTimeout);
        }
        catch (ArgumentException ex)
        {
            // Not an expression at all.
            RefusePattern(path, ex.Message);
            return null;
        }
        catch (NotSupportedException ex)
        {
            // An expression, but one that only backtracking can run.
            RefusePattern(path, ex.Message);
            return null;
        }
    }

    /// <summary>Reads <c>enum</c>.</summary>
    /// <param name="element">The node.</param>
    /// <param name="path">Where it sits.</param>
    /// <returns>The admitted values, or null when the node admits any of its type.</returns>
    public IReadOnlyList<JsonElement>? ReadAllowedValues(JsonElement element, string path)
    {
        if (!element.TryGetProperty("enum", out var choices))
            return null;

        if (choices.ValueKind is not JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            _log.Refuse(
                JsonPointer.Append(path, "enum"),
                SchemaCodes.SchemaKeywordMalformed,
                "\"enum\" must be a non-empty array of the values the field admits.");

            return null;
        }

        return choices.EnumerateArray().Select(choice => choice.Clone()).ToList();
    }

    /// <summary>Reads one of the whole-number keywords.</summary>
    /// <param name="element">The node.</param>
    /// <param name="path">Where it sits.</param>
    /// <param name="keyword">Which one.</param>
    /// <returns>The count, or null.</returns>
    public int? ReadCount(JsonElement element, string path, string keyword)
    {
        if (!element.TryGetProperty(keyword, out var value))
            return null;

        if (value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out var count) && count >= 0)
            return count;

        _log.Refuse(
            JsonPointer.Append(path, keyword),
            SchemaCodes.SchemaKeywordMalformed,
            $"\"{keyword}\" must be a whole number of zero or more.");

        return null;
    }

    /// <summary>Reads one of the numeric keywords.</summary>
    /// <param name="element">The node.</param>
    /// <param name="path">Where it sits.</param>
    /// <param name="keyword">Which one.</param>
    /// <returns>The bound, or null.</returns>
    public decimal? ReadBound(JsonElement element, string path, string keyword)
    {
        if (!element.TryGetProperty(keyword, out var value))
            return null;

        if (value.ValueKind is JsonValueKind.Number && value.TryGetDecimal(out var bound))
            return bound;

        _log.Refuse(JsonPointer.Append(path, keyword), SchemaCodes.SchemaKeywordMalformed, $"\"{keyword}\" must be a number.");

        return null;
    }

    private static bool IsKnownFormat(string name) => name is SchemaFormats.Email
        or SchemaFormats.Uri
        or SchemaFormats.Date
        or SchemaFormats.DateTime
        or SchemaFormats.Uuid
        or SchemaFormats.TextArea
        or SchemaFormats.Markdown;

    private void RefusePattern(string path, string reason)
        => _log.Refuse(
            JsonPointer.Append(path, "pattern"),
            SchemaCodes.SchemaPatternNotSupported,
            $"The pattern is not one this platform will run: {reason}");
}
