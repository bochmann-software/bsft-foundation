namespace Bsft.Schema;

/// <summary>The codes a refusal carries, so a caller can react to one without reading its message.</summary>
/// <remarks>
/// Split in two on purpose. The <c>SCHEMA_</c> codes are defects in a document a developer submitted
/// and belong in a review queue; the <c>VALUE_</c> codes are a tenant's configuration not matching a
/// schema that is itself fine, and belong on an install screen. The two are shown to different people
/// and one of them is a support ticket.
/// </remarks>
public static class SchemaCodes
{
    /// <summary>The schema is not a JSON object, or its root is not <c>type: "object"</c>.</summary>
    public const string SchemaRootNotAnObject = "SCHEMA_ROOT_NOT_AN_OBJECT";

    /// <summary>A node below the root is written as something other than a JSON object.</summary>
    /// <remarks>
    /// Separate from <see cref="SchemaRootNotAnObject"/> because the two are different repairs. A root
    /// that is not an object means the submitted document is not a schema at all; a node that is not one
    /// means a single property was written as its value instead of as a schema for that value, and the
    /// refusal's path names which. A caller giving the root case its own handling would otherwise have
    /// to read the path to tell them apart.
    /// </remarks>
    public const string SchemaNodeNotAnObject = "SCHEMA_NODE_NOT_AN_OBJECT";

    /// <summary>A keyword outside the supported subset appears in the schema.</summary>
    public const string SchemaKeywordNotSupported = "SCHEMA_KEYWORD_NOT_SUPPORTED";

    /// <summary>A keyword is present but its value is not of the shape that keyword takes.</summary>
    public const string SchemaKeywordMalformed = "SCHEMA_KEYWORD_MALFORMED";

    /// <summary>A schema node has no <c>type</c>, or names one outside the six.</summary>
    public const string SchemaTypeNotSupported = "SCHEMA_TYPE_NOT_SUPPORTED";

    /// <summary>A <c>format</c> outside the supported list.</summary>
    public const string SchemaFormatNotSupported = "SCHEMA_FORMAT_NOT_SUPPORTED";

    /// <summary>A <c>pattern</c> that is not a regular expression this validator will run.</summary>
    public const string SchemaPatternNotSupported = "SCHEMA_PATTERN_NOT_SUPPORTED";

    /// <summary>The schema is larger, deeper or wider than the configured bounds allow.</summary>
    public const string SchemaTooLarge = "SCHEMA_TOO_LARGE";

    /// <summary>A required property is not among the declared properties.</summary>
    public const string SchemaRequiredUnknown = "SCHEMA_REQUIRED_UNKNOWN";

    /// <summary>The document is not of the type the schema declares at this path.</summary>
    public const string ValueTypeMismatch = "VALUE_TYPE_MISMATCH";

    /// <summary>A required property is missing.</summary>
    public const string ValueRequired = "VALUE_REQUIRED";

    /// <summary>A property the schema does not declare is present.</summary>
    public const string ValueUnknownProperty = "VALUE_UNKNOWN_PROPERTY";

    /// <summary>The value is not one of the declared choices.</summary>
    public const string ValueNotAllowed = "VALUE_NOT_ALLOWED";

    /// <summary>A string is shorter or longer than the declared bounds.</summary>
    public const string ValueLengthOutOfRange = "VALUE_LENGTH_OUT_OF_RANGE";

    /// <summary>A number is below the minimum or above the maximum.</summary>
    public const string ValueOutOfRange = "VALUE_OUT_OF_RANGE";

    /// <summary>An array has too few or too many items.</summary>
    public const string ValueItemCountOutOfRange = "VALUE_ITEM_COUNT_OUT_OF_RANGE";

    /// <summary>A string does not match the declared format.</summary>
    public const string ValueFormatMismatch = "VALUE_FORMAT_MISMATCH";

    /// <summary>A string does not match the declared pattern.</summary>
    public const string ValuePatternMismatch = "VALUE_PATTERN_MISMATCH";

    /// <summary>The document is deeper or larger than the configured bounds allow.</summary>
    public const string ValueTooLarge = "VALUE_TOO_LARGE";
}
