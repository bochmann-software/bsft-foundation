using System.Globalization;
using System.Text;

namespace Bsft.Core;

/// <summary>The data of Unicode normalization forms C and D and the Default_Ignorable_Code_Point property, from the generated Unicode data.</summary>
/// <remarks>
/// Data, not a platform call, on purpose: <see cref="string.Normalize(NormalizationForm)"/> asks ICU on Linux and the
/// operating system on Windows, does nothing at all under invariant globalization and knows the Unicode version of the
/// host, so one name would get different A-labels and different skeletons on two hosts sharing a database.
/// </remarks>
internal sealed class UnicodeNormalizationTable
{
    private const string Resource = "Bsft.Core.Data.idna-normalization.txt";
    private const int PairShift = 21;

    private static readonly Lazy<UnicodeNormalizationTable> Shared = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly CodePointMap<byte> _classes;
    private readonly Dictionary<int, string> _decompositions;
    private readonly Dictionary<long, int> _compositions;
    private readonly CodePointMap<bool> _ignorable;

    private UnicodeNormalizationTable(
        CodePointMap<byte> classes,
        Dictionary<int, string> decompositions,
        Dictionary<long, int> compositions,
        CodePointMap<bool> ignorable)
    {
        _classes = classes;
        _decompositions = decompositions;
        _compositions = compositions;
        _ignorable = ignorable;
    }

    /// <summary>Gets the table, loaded on first use.</summary>
    public static UnicodeNormalizationTable Instance => Shared.Value;

    /// <summary>Gets the canonical combining class of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The class; zero for a starter.</returns>
    public int GetCombiningClass(int codePoint) => _classes.TryGetValue(codePoint, out var value) ? value : 0;

    /// <summary>Looks up the full canonical decomposition of a code point (Hangul syllables are not listed).</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="decomposition">The code points it decomposes into, themselves fully decomposed.</param>
    /// <returns><see langword="true"/> when the code point decomposes.</returns>
    public bool TryGetDecomposition(int codePoint, out string? decomposition) => _decompositions.TryGetValue(codePoint, out decomposition);

    /// <summary>Looks up the primary composite of two code points.</summary>
    /// <param name="first">The starter.</param>
    /// <param name="second">The code point that follows it.</param>
    /// <param name="composite">The composite when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the pair composes (Hangul pairs are not listed).</returns>
    public bool TryGetComposite(int first, int second, out int composite) => _compositions.TryGetValue(Pair(first, second), out composite);

    /// <summary>Tells whether a code point has the Default_Ignorable_Code_Point property.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> when it has.</returns>
    public bool IsDefaultIgnorable(int codePoint) => _ignorable.Contains(codePoint);

    private static long Pair(int first, int second) => ((long)first << PairShift) | (uint)second;

    private static UnicodeNormalizationTable Load()
    {
        var raw = IdnaResource.ReadRanges(Resource, "[decomposition]").ToDictionary(range => range.First, range => IdnaResource.ParseSequence(range.Text));
        var exclusions = new CodePointMap<bool>(IdnaResource.ReadRanges(Resource, "[exclusions]").Select(range => (range.First, range.Last, true)).ToList());
        var compositions = new Dictionary<long, int>();

        foreach (var (codePoint, decomposition) in raw)
        {
            var runes = decomposition.EnumerateRunes().ToArray();

            if (runes.Length == 2 && !exclusions.Contains(codePoint))
                compositions[Pair(runes[0].Value, runes[1].Value)] = codePoint;
        }

        var full = new Dictionary<int, string>();

        foreach (var codePoint in raw.Keys)
            full[codePoint] = Resolve(codePoint, raw);

        return new UnicodeNormalizationTable(
            new CodePointMap<byte>(IdnaResource.ReadRanges(Resource, "[ccc]").Select(range => (range.First, range.Last, byte.Parse(range.Text, CultureInfo.InvariantCulture))).ToList()),
            full,
            compositions,
            new CodePointMap<bool>(IdnaResource.ReadRanges(Resource, "[ignorable]").Select(range => (range.First, range.Last, true)).ToList()));
    }

    private static string Resolve(int codePoint, Dictionary<int, string> raw)
    {
        var output = new StringBuilder();

        foreach (var rune in raw[codePoint].EnumerateRunes())
            output.Append(raw.ContainsKey(rune.Value) ? Resolve(rune.Value, raw) : rune.ToString());

        return output.ToString();
    }
}
