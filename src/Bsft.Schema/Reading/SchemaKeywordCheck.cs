using System.Text.Json;

namespace Bsft.Schema.Reading;

/// <summary>Refuses, by name, every keyword the subset does not understand on this node.</summary>
/// <remarks>
/// The one decision here worth arguing about, and it is the safe direction: a schema writing
/// <c>maxLenght</c> declares a bound the platform then does not enforce, on a page that says the field
/// is validated. Ignoring it is the silent failure. A developer finds out at submission, where a typo
/// is cheap.
/// </remarks>
internal sealed class SchemaKeywordCheck
{
    private readonly SchemaViolationLog _log;

    /// <summary>Initializes a new instance of the <see cref="SchemaKeywordCheck"/> class.</summary>
    /// <param name="log">Where a refusal goes.</param>
    public SchemaKeywordCheck(SchemaViolationLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Refuses every keyword on this node that is not in the subset for its kind.</summary>
    /// <param name="element">The submitted node.</param>
    /// <param name="path">Where it sits.</param>
    /// <param name="kind">What it declared itself to be.</param>
    public void RefuseUnknown(JsonElement element, string path, SchemaValueKind kind)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!IsKnown(property.Name, kind))
            {
                _log.Refuse(
                    JsonPointer.Append(path, property.Name),
                    SchemaCodes.SchemaKeywordNotSupported,
                    $"The keyword \"{property.Name}\" is not one this platform supports here.");
            }
        }
    }

    private static bool IsKnown(string keyword, SchemaValueKind kind)
        => SchemaKeywords.Annotations.Contains(keyword)
            || SchemaKeywords.Common.Contains(keyword)
            || ForKind(kind).Contains(keyword);

    // Object is the default arm rather than a case of its own: every kind is named here, and an arm the
    // compiler cannot prove unreachable is an arm no test can reach either.
    private static IReadOnlySet<string> ForKind(SchemaValueKind kind) => kind switch
    {
        SchemaValueKind.String => SchemaKeywords.String,
        SchemaValueKind.Integer or SchemaValueKind.Number => SchemaKeywords.Numeric,
        SchemaValueKind.Array => SchemaKeywords.Array,
        SchemaValueKind.Boolean => SchemaKeywords.None,
        _ => SchemaKeywords.Object,
    };
}
