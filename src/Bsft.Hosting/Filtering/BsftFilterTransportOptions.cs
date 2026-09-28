namespace Bsft.Hosting.Filtering;

/// <summary>
/// Bounds for the CRUD list convention's <c>filter</c> query parameter, validated eagerly at
/// <c>AddBsftFilterTransport</c>. The filter is client-controlled JSON that deserializes into the
/// polymorphic filter AST, so every limit here exists to keep a hostile or accidental filter from
/// turning into a denial of service before it ever reaches the whitelist-checked compiler.
/// </summary>
/// <remarks>
/// Nothing here is read ad hoc from configuration elsewhere: a value that governs how a filter is
/// accepted lives here, is validated here, and is named in the message when it cannot be used. The
/// limits form a defence in depth — <see cref="MaxJsonReaderDepth"/> stops the deserializer's own
/// recursion before it can overflow the stack, while <see cref="MaxDepth"/> and
/// <see cref="MaxNodeCount"/> reject an oversized-but-well-formed AST with a clear message before it
/// is compiled into an expression tree.
/// </remarks>
public sealed class BsftFilterTransportOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Hosting:Filtering";

    /// <summary>The default ceiling on the length of the raw <c>filter</c> query value, in characters.</summary>
    public const int DefaultMaxFilterLength = 8192;

    /// <summary>The default hard recursion limit handed to the JSON reader.</summary>
    public const int DefaultMaxJsonReaderDepth = 64;

    /// <summary>The default ceiling on the nesting depth of the filter AST.</summary>
    public const int DefaultMaxDepth = 24;

    /// <summary>The default ceiling on the total number of nodes in the filter AST.</summary>
    public const int DefaultMaxNodeCount = 200;

    /// <summary>
    /// Gets or sets the maximum length, in characters, of the raw <c>filter</c> query value. A longer
    /// value is rejected before any JSON is parsed, so an oversized string never reaches the
    /// deserializer. Defaults to <see cref="DefaultMaxFilterLength"/>.
    /// </summary>
    public int MaxFilterLength { get; set; } = DefaultMaxFilterLength;

    /// <summary>
    /// Gets or sets the hard recursion limit handed to <c>System.Text.Json</c> as its
    /// <c>MaxDepth</c>. This is the guard that keeps a deeply nested payload from overflowing the
    /// stack inside the deserializer itself; a payload that exceeds it is a well-formed 400, never a
    /// crash. It must be at least as large as <see cref="MaxDepth"/> so the friendlier AST-depth check
    /// is reachable. Defaults to <see cref="DefaultMaxJsonReaderDepth"/>.
    /// </summary>
    public int MaxJsonReaderDepth { get; set; } = DefaultMaxJsonReaderDepth;

    /// <summary>
    /// Gets or sets the maximum nesting depth of the filter AST — the deepest chain of nested
    /// <c>and</c>/<c>or</c> combinations. An AST deeper than this is rejected with a clear message
    /// before it is compiled. Defaults to <see cref="DefaultMaxDepth"/>.
    /// </summary>
    public int MaxDepth { get; set; } = DefaultMaxDepth;

    /// <summary>
    /// Gets or sets the maximum total number of nodes in the filter AST. A broad-but-shallow filter
    /// with more nodes than this is rejected before it is compiled, bounding the size of the
    /// expression tree the compiler builds. Defaults to <see cref="DefaultMaxNodeCount"/>.
    /// </summary>
    public int MaxNodeCount { get; set; } = DefaultMaxNodeCount;

    /// <summary>
    /// Validates the options.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when a value cannot be used as it stands.</exception>
    internal void Validate()
    {
        RequirePositive(MaxFilterLength, nameof(MaxFilterLength));
        RequirePositive(MaxJsonReaderDepth, nameof(MaxJsonReaderDepth));
        RequirePositive(MaxDepth, nameof(MaxDepth));
        RequirePositive(MaxNodeCount, nameof(MaxNodeCount));

        if (MaxJsonReaderDepth < MaxDepth)
            throw Invalid(
                nameof(MaxJsonReaderDepth),
                $"must be at least {nameof(MaxDepth)} ({MaxDepth}); otherwise the JSON reader rejects a filter before the AST-depth check can, and the client gets a less specific error. A nested AST costs roughly two JSON levels per depth, so keep this comfortably above twice the AST depth.");
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
            throw Invalid(name, "must be positive.");
    }

    private static InvalidOperationException Invalid(string name, string problem)
        => new($"{nameof(BsftFilterTransportOptions)}.{name} {problem}");
}
