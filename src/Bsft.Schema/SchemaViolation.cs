namespace Bsft.Schema;

/// <summary>One refusal: where, why, and in words the person reading it can act on.</summary>
/// <remarks>
/// <para>
/// This is shown on an install screen rather than written to a log, so it carries all three. The
/// <see cref="Path"/> is what makes a refusal actionable in a document with forty fields, the
/// <see cref="Code"/> is what a caller branches on, and the <see cref="Message"/> is the only part a
/// person reads. A refusal with only a code is a support ticket.
/// </para>
/// <para>
/// The path is a JSON Pointer (RFC 6901): <c>""</c> for the document itself, <c>"/smtp/port"</c> for a
/// nested property, <c>"/hosts/2"</c> for an array element. A pointer rather than a dotted path
/// because a property name may itself contain a dot, and a path that cannot be parsed back is a path
/// a form cannot highlight the right field from.
/// </para>
/// </remarks>
public sealed class SchemaViolation
{
    /// <summary>Gets the JSON Pointer to what was refused.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the code, from <see cref="SchemaCodes"/>.</summary>
    public required string Code { get; init; }

    /// <summary>Gets the sentence a person reads.</summary>
    public required string Message { get; init; }
}
