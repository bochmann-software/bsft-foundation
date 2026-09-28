using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Bsft.Schema.ValueValidation;

/// <summary>The shipped validator: every refusal, each naming a path a form can highlight.</summary>
/// <remarks>
/// Holds no state of its own between calls. Each call runs on its own pass object, so the depth and
/// breadth budgets belong to that document rather than to whichever document arrived first.
/// </remarks>
internal sealed class SchemaValidator : ISchemaValidator
{
    private readonly IOptionsMonitor<BsftSchemaOptions> _options;

    /// <summary>Initializes a new instance of the <see cref="SchemaValidator"/> class.</summary>
    /// <param name="options">The bounds a submitted document is read within.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public SchemaValidator(IOptionsMonitor<BsftSchemaOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public SchemaValidationResult Validate(SchemaNode schema, JsonElement document)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var log = new SchemaViolationLog();

        // Composed here rather than inside the pass: both halves share that checking's own log, and
        // neither outlives the call.
        var pass = new SchemaValidationPass(_options.CurrentValue, log, new SchemaValueCheck(log));

        pass.Check(schema, document, path: string.Empty, depth: 1);

        return new SchemaValidationResult { Violations = log.Violations };
    }
}
