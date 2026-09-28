using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Bsft.Schema.Reading;

/// <summary>The shipped reader: the subset, the bounds, and a named refusal for everything else.</summary>
/// <remarks>
/// <para>
/// <strong>Unknown keywords are refused, not ignored.</strong> A schema that writes <c>maxLenght</c>
/// declares a bound the platform then does not enforce, on a page that says the field is validated. A
/// developer finds out at submission, which is where a typo is cheap.
/// </para>
/// <para>
/// <strong>Additional properties are never admitted.</strong> <c>additionalProperties</c> may be
/// written as <see langword="false"/> and is <see langword="false"/> when absent; <c>true</c> is
/// refused. A configuration object with room in it for anything is one nothing downstream can read,
/// and in a store it is also where a tenant's own data ends up in a document the developer can read
/// back.
/// </para>
/// <para>
/// Reading is where a schema is bounded and where a pattern is compiled, so a validator run against
/// the result cannot be made to recurse, allocate or backtrack by the schema.
/// </para>
/// </remarks>
internal sealed class SchemaReader : ISchemaReader
{
    private readonly IOptionsMonitor<BsftSchemaOptions> _options;

    /// <summary>Initializes a new instance of the <see cref="SchemaReader"/> class.</summary>
    /// <param name="options">The bounds a submitted schema is read within.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public SchemaReader(IOptionsMonitor<BsftSchemaOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public SchemaReadResult Read(JsonElement schema)
    {
        var bounds = _options.CurrentValue;
        var log = new SchemaViolationLog();

        // Composed here rather than inside the pass: everything taking part in one reading shares that
        // reading's own log and budget, and none of them outlives the call.
        var pass = new SchemaReadPass(
            log,
            new SchemaBudget(bounds, log),
            new SchemaKeywordCheck(log),
            new SchemaConstraintReader(bounds, log));

        var root = pass.ReadNode(schema, path: string.Empty, depth: 1);

        if (root is not null && root.Kind is not SchemaValueKind.Object)
        {
            log.Refuse(string.Empty, SchemaCodes.SchemaRootNotAnObject, "The root of a schema must be an object.");
            root = null;
        }

        return new SchemaReadResult
        {
            Schema = log.Count == 0 ? root : null,
            Violations = log.Violations,
        };
    }
}
