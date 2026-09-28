namespace Bsft.Schema.Reading;

/// <summary>How much of a submitted schema one reading is willing to walk.</summary>
/// <remarks>
/// A schema nested four hundred deep is an input, not an accident, and the refusal has to arrive as a
/// refusal rather than as a stack overflow on a request thread. The node count is spent across the
/// whole reading rather than per branch, so a schema that is wide instead of deep is bounded too.
/// </remarks>
internal sealed class SchemaBudget
{
    private readonly BsftSchemaOptions _bounds;
    private readonly SchemaViolationLog _log;
    private int _nodes;

    /// <summary>Initializes a new instance of the <see cref="SchemaBudget"/> class.</summary>
    /// <param name="bounds">The ceilings this reading is held to.</param>
    /// <param name="log">Where a refusal goes.</param>
    public SchemaBudget(BsftSchemaOptions bounds, SchemaViolationLog log)
    {
        _bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Gets how many properties one object may declare.</summary>
    public int MaxProperties => _bounds.MaxProperties;

    /// <summary>Spends one node of the budget.</summary>
    /// <param name="path">Where the node sits.</param>
    /// <param name="depth">How deep it sits, the root being one.</param>
    /// <returns>Whether the reading may continue into it.</returns>
    public bool IsWithinBudget(string path, int depth)
    {
        if (depth > _bounds.MaxDepth)
        {
            _log.Refuse(path, SchemaCodes.SchemaTooLarge, $"The schema nests deeper than {_bounds.MaxDepth} levels.");
            return false;
        }

        if (++_nodes <= _bounds.MaxSchemaNodes)
            return true;

        _log.Refuse(path, SchemaCodes.SchemaTooLarge, $"The schema declares more than {_bounds.MaxSchemaNodes} nodes.");

        return false;
    }

    /// <summary>Refuses an object that declares more properties than the ceiling allows.</summary>
    /// <param name="path">Where the object sits.</param>
    public void RefuseTooManyProperties(string path)
        => _log.Refuse(path, SchemaCodes.SchemaTooLarge, $"An object may declare at most {_bounds.MaxProperties} properties.");
}
