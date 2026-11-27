using System.Text;

namespace Bsft.Core;

/// <summary>Unicode normalization forms C and D, and the removal of default-ignorable code points, from data embedded in this assembly.</summary>
/// <remarks>
/// The framework's keys (a canonical domain, a look-alike skeleton) are derived from normalized text and stored, so they
/// must not depend on the host: <see cref="string.Normalize(NormalizationForm)"/> answers from ICU or the operating
/// system, does nothing under invariant globalization and knows whichever Unicode version the host ships. These methods
/// implement UAX 15 over tables generated from the Unicode version pinned in <c>tools/unicode-data/generate.py</c>, and
/// give the same answer everywhere. A lone surrogate is read as U+FFFD.
/// </remarks>
public static class UnicodeNormalization
{
    private const int HangulBase = 0xAC00;
    private const int LeadBase = 0x1100;
    private const int VowelBase = 0x1161;
    private const int TrailBase = 0x11A7;
    private const int LeadCount = 19;
    private const int VowelCount = 21;
    private const int TrailCount = 28;
    private const int VowelTrailCount = VowelCount * TrailCount;
    private const int SyllableCount = LeadCount * VowelTrailCount;

    /// <summary>Converts a string to normalization form D (canonical decomposition, canonical ordering).</summary>
    /// <param name="text">The string.</param>
    /// <returns>The decomposed string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static string ToFormD(this string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.All(char.IsAscii) ? text : ToText(Decompose(text));
    }

    /// <summary>Converts a string to normalization form C (canonical decomposition, then canonical composition).</summary>
    /// <param name="text">The string.</param>
    /// <returns>The composed string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static string ToFormC(this string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.All(char.IsAscii) ? text : ToText(Compose(Decompose(text)));
    }

    /// <summary>Tells whether a string is in normalization form C.</summary>
    /// <param name="text">The string.</param>
    /// <returns><see langword="true"/> when composing it changes nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static bool IsFormC(this string text) => string.Equals(text.ToFormC(), text, StringComparison.Ordinal);

    /// <summary>Removes the code points with the Default_Ignorable_Code_Point property (joiners, variation selectors, soft hyphen and the like).</summary>
    /// <param name="text">The string.</param>
    /// <returns>The string without them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static string RemoveDefaultIgnorables(this string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var output = new StringBuilder(text.Length);

        foreach (var rune in text.EnumerateRunes().Where(rune => !UnicodeNormalizationTable.Instance.IsDefaultIgnorable(rune.Value)))
            output.Append(rune.ToString());

        return output.ToString();
    }

    private static string ToText(List<int> codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    private static List<int> Decompose(string text)
    {
        var codePoints = new List<int>(text.Length);

        foreach (var rune in text.EnumerateRunes())
            AppendDecomposed(rune.Value, codePoints);

        // Canonical ordering: a stable sort of each run of combining marks by combining class.
        for (var i = 1; i < codePoints.Count; i++)
        {
            var current = codePoints[i];
            var combiningClass = UnicodeNormalizationTable.Instance.GetCombiningClass(current);
            var j = i;

            while (combiningClass != 0 && j > 0 && UnicodeNormalizationTable.Instance.GetCombiningClass(codePoints[j - 1]) > combiningClass)
            {
                codePoints[j] = codePoints[j - 1];
                j--;
            }

            codePoints[j] = current;
        }

        return codePoints;
    }

    private static void AppendDecomposed(int codePoint, List<int> output)
    {
        var syllable = codePoint - HangulBase;

        if (syllable is >= 0 and < SyllableCount)
        {
            output.Add(LeadBase + (syllable / VowelTrailCount));
            output.Add(VowelBase + (syllable % VowelTrailCount / TrailCount));

            if (syllable % TrailCount != 0)
                output.Add(TrailBase + (syllable % TrailCount));
        }
        else if (UnicodeNormalizationTable.Instance.TryGetDecomposition(codePoint, out var decomposition))
        {
            output.AddRange(decomposition!.EnumerateRunes().Select(rune => rune.Value));
        }
        else
        {
            output.Add(codePoint);
        }
    }

    // UAX 15, canonical composition: a mark composes into the last starter unless a mark of the same or a higher class,
    // or another starter, stands between them.
    private static List<int> Compose(List<int> decomposed)
    {
        var result = new List<int>(decomposed.Count);
        var starter = -1;
        var lastClass = 0;

        foreach (var codePoint in decomposed)
        {
            var combiningClass = UnicodeNormalizationTable.Instance.GetCombiningClass(codePoint);

            if (starter >= 0 && (lastClass < combiningClass || lastClass == 0) && TryCompose(result[starter], codePoint, out var composite))
            {
                result[starter] = composite;

                continue;
            }

            if (combiningClass == 0)
                starter = result.Count;

            lastClass = combiningClass;
            result.Add(codePoint);
        }

        return result;
    }

    private static bool TryCompose(int first, int second, out int composite)
    {
        var lead = first - LeadBase;
        var vowel = second - VowelBase;
        var syllable = first - HangulBase;
        var trail = second - TrailBase;

        if (lead is >= 0 and < LeadCount && vowel is >= 0 and < VowelCount)
        {
            composite = HangulBase + (((lead * VowelCount) + vowel) * TrailCount);

            return true;
        }

        if (syllable is >= 0 and < SyllableCount && syllable % TrailCount == 0 && trail is > 0 and < TrailCount)
        {
            composite = first + trail;

            return true;
        }

        return UnicodeNormalizationTable.Instance.TryGetComposite(first, second, out composite);
    }
}
