namespace Bsft.Schema;

/// <summary>What one reading or one checking refused, collected in the order it was found.</summary>
/// <remarks>
/// Shared by everything taking part in one call, which is what lets a reading be split across several
/// focused collaborators and still answer with one list. A per-call object rather than state on a
/// registered service: the reader and the validator are singletons, and a list shared between two
/// concurrent calls would report one document's refusals against the other.
/// </remarks>
internal sealed class SchemaViolationLog
{
    private readonly List<SchemaViolation> _violations = [];

    /// <summary>Gets what was refused.</summary>
    public IReadOnlyList<SchemaViolation> Violations => _violations;

    /// <summary>Gets how much has been refused so far.</summary>
    /// <remarks>
    /// Read before and after a stretch of work, which is how a caller tells "this produced a node" from
    /// "this produced a node and four reasons not to use it" without threading a result type through
    /// every step.
    /// </remarks>
    public int Count => _violations.Count;

    /// <summary>Records a refusal.</summary>
    /// <param name="path">The JSON Pointer to what was refused.</param>
    /// <param name="code">The code, from <see cref="SchemaCodes"/>.</param>
    /// <param name="message">The sentence a person reads.</param>
    public void Refuse(string path, string code, string message)
        => _violations.Add(new SchemaViolation { Path = path, Code = code, Message = message });
}
