using System.Text.Json;

namespace Bsft.Schema;

/// <summary>Reads a schema a developer submitted, refusing everything outside the subset.</summary>
/// <remarks>
/// <para>
/// A schema is an input, not a configuration file: it arrives with an application somebody uploaded,
/// and the platform runs it against every tenant that installs that application. So this is where the
/// suspicion lives. Every keyword is either understood or refused by name, and nothing is ignored: a
/// silently dropped <c>maxLenght</c> is an unvalidated field that reads on the install screen as
/// though it were validated.
/// </para>
/// <para>
/// Reading is also where a schema is bounded and where a pattern is compiled, so a validator run
/// against the result cannot be made to recurse, allocate or backtrack by the schema.
/// </para>
/// </remarks>
public interface ISchemaReader
{
    /// <summary>Reads one schema document.</summary>
    /// <param name="schema">The submitted schema.</param>
    /// <returns>The schema, or the refusals. Never both.</returns>
    SchemaReadResult Read(JsonElement schema);
}
