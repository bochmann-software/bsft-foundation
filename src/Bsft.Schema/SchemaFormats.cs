namespace Bsft.Schema;

/// <summary>The <c>format</c> values this subset understands.</summary>
/// <remarks>
/// <para>
/// Five checked and two presentational. The five are the ones a platform can check without guessing:
/// an email, an absolute URI, a date, a date and time, and a UUID. The two presentational ones say how
/// a long string is edited and constrain nothing, which is why they are listed here rather than left
/// to a sibling UI document: a schema that carries them is still inside the subset, and a validator
/// that refused them would refuse manifests that are otherwise fine.
/// </para>
/// <para>
/// Anything else is refused rather than ignored. A silently unrecognised <c>format</c> is an
/// unvalidated input that reads on the page as though it were validated.
/// </para>
/// </remarks>
public static class SchemaFormats
{
    /// <summary>An email address.</summary>
    public const string Email = "email";

    /// <summary>An absolute URI.</summary>
    public const string Uri = "uri";

    /// <summary>A calendar date, <c>yyyy-MM-dd</c>.</summary>
    public const string Date = "date";

    /// <summary>A date and time with an offset, RFC 3339.</summary>
    public const string DateTime = "date-time";

    /// <summary>A UUID.</summary>
    public const string Uuid = "uuid";

    /// <summary>Presentational: a multi-line string. Constrains nothing.</summary>
    public const string TextArea = "textarea";

    /// <summary>Presentational: a string edited as Markdown. Constrains nothing.</summary>
    public const string Markdown = "markdown";
}
