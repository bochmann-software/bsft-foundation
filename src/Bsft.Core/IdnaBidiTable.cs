namespace Bsft.Core;

/// <summary>The bidirectional class of every code point, from the generated Unicode data.</summary>
internal sealed class IdnaBidiTable
{
    private const string Resource = "Bsft.Core.Data.idna-validation.txt";

    private static readonly Lazy<IdnaBidiTable> Shared = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly CodePointMap<BidiClass> _classes;

    private IdnaBidiTable(CodePointMap<BidiClass> classes) => _classes = classes;

    /// <summary>Gets the table, loaded on first use.</summary>
    public static IdnaBidiTable Instance => Shared.Value;

    /// <summary>Gets the bidirectional class of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The class; <see cref="BidiClass.L"/> for a code point the data does not list.</returns>
    public BidiClass GetClass(int codePoint) => _classes.TryGetValue(codePoint, out var value) ? value : BidiClass.L;

    private static IdnaBidiTable Load()
        => new(new CodePointMap<BidiClass>(
            IdnaResource.ReadRanges(Resource, "[bidi]")
                .Select(range => (range.First, range.Last, Enum.TryParse<BidiClass>(range.Text, out var bidiClass) ? bidiClass : BidiClass.Other))
                .ToList()));
}
