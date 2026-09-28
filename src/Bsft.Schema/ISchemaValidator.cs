using System.Text.Json;

namespace Bsft.Schema;

/// <summary>Checks a document against a schema that has already been read.</summary>
/// <remarks>
/// <para>
/// The schema is <see cref="SchemaNode"/> rather than raw JSON, which is what makes the two failures
/// distinguishable: a schema outside the subset never reaches here, so every refusal from this call is
/// about the document. One of the two is a developer's problem and the other is a tenant admin's, and
/// telling them apart by which call refused is more reliable than telling them apart by a code.
/// </para>
/// <para>
/// The document is bounded too. A schema with no <c>maxItems</c> still has the host's ceiling, and a
/// document nested past the configured depth is refused rather than followed.
/// </para>
/// </remarks>
public interface ISchemaValidator
{
    /// <summary>Checks one document.</summary>
    /// <param name="schema">The schema, from <see cref="ISchemaReader"/>.</param>
    /// <param name="document">The document a tenant submitted.</param>
    /// <returns>Every way the document does not match, or nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="schema"/> is null.</exception>
    SchemaValidationResult Validate(SchemaNode schema, JsonElement document);
}
