using System.Text.Json;

namespace Bsft.Scheduling;

/// <summary>
/// What a job reported about one of its runs, such as how many items it processed and how many of
/// them failed, stored with the run and returned by the run listing.
/// </summary>
/// <remarks>
/// <para>
/// The value is held as JSON rather than as the job's own type: a listing crosses every job at once
/// and cannot know what any one of them reports, so it hands the JSON through and the
/// <see cref="Schema"/> tells a reader how to interpret it. A schema is a name the job owns, such as
/// <c>mail-dispatch/v1</c>, and changing the shape of the value is a reason to change the name.
/// A store keeps the schema and the JSON text of the value (<see cref="JsonElement.GetRawText"/>), and
/// reads it back by deserializing that text into a <see cref="JsonElement"/>.
/// </para>
/// <para>
/// A result is metadata, not content. The run listing returns it under the metadata permission, so a
/// job records counts, sizes and identifiers of its own making here, never the data it worked on.
/// Whatever an operator must not see without an audited read belongs in the job's own store.
/// </para>
/// </remarks>
public sealed record JobRunResult
{
    /// <summary>The longest schema name a result may carry, which is the width of its stored column.</summary>
    public const int MaxSchemaLength = 200;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobRunResult"/> class.
    /// </summary>
    /// <param name="schema">The name that says how <paramref name="value"/> is to be read.</param>
    /// <param name="value">The reported value as JSON.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="schema"/> is blank or longer than <see cref="MaxSchemaLength"/>, or when
    /// <paramref name="value"/> holds no JSON value at all.
    /// </exception>
    public JobRunResult(string schema, JsonElement value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        if (schema.Length > MaxSchemaLength)
        {
            throw new ArgumentException(
                $"A job run result schema may be at most {MaxSchemaLength} characters long, but it is {schema.Length}.",
                nameof(schema));
        }

        if (value.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("A job run result needs a JSON value.", nameof(value));

        Schema = schema;
        Value = value;
    }

    /// <summary>Gets the name that says how <see cref="Value"/> is to be read.</summary>
    public string Schema { get; }

    /// <summary>Gets the reported value as JSON.</summary>
    public JsonElement Value { get; }

    /// <summary>
    /// Determines whether another result carries the same schema and the same JSON text.
    /// </summary>
    /// <remarks>
    /// Written out because a <see cref="JsonElement"/> compares by the document it was read from, so two
    /// results read from the same stored row would otherwise be unequal.
    /// </remarks>
    /// <param name="other">The result to compare with.</param>
    /// <returns><see langword="true"/> when both name the same schema and hold the same JSON text.</returns>
    public bool Equals(JobRunResult? other) =>
        other is not null
        && string.Equals(Schema, other.Schema, StringComparison.Ordinal)
        && string.Equals(Value.GetRawText(), other.Value.GetRawText(), StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(Schema),
            StringComparer.Ordinal.GetHashCode(Value.GetRawText()));
}
