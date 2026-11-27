namespace Bsft.Core;

/// <summary>The combining marks (general category Mn, Mc and Me), from the generated Unicode data.</summary>
internal sealed class IdnaMarkTable
{
    private const string Resource = "Bsft.Core.Data.idna-validation.txt";

    private static readonly Lazy<IdnaMarkTable> Shared = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly CodePointMap<bool> _marks;

    private IdnaMarkTable(CodePointMap<bool> marks) => _marks = marks;

    /// <summary>Gets the table, loaded on first use.</summary>
    public static IdnaMarkTable Instance => Shared.Value;

    /// <summary>Tells whether a code point is a combining mark.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> for a mark.</returns>
    public bool IsMark(int codePoint) => _marks.Contains(codePoint);

    private static IdnaMarkTable Load()
        => new(new CodePointMap<bool>(IdnaResource.ReadRanges(Resource, "[marks]").Select(range => (range.First, range.Last, true)).ToList()));
}
