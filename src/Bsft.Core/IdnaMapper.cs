using System.Text;

namespace Bsft.Core;

/// <summary>Step 1 of UTS 46 processing: the mapping table, applied to a whole name, followed by Unicode normalization form C (<see cref="UnicodeNormalization"/>, not the platform's).</summary>
internal static class IdnaMapper
{
    /// <summary>Maps a name.</summary>
    /// <param name="input">The name as it was given.</param>
    /// <param name="mapped">The name with every code point mapped, ignored ones removed, in form C.</param>
    /// <returns><see langword="false"/> when a code point is disallowed or not a letter, digit, hyphen or dot after mapping.</returns>
    public static bool TryMap(string input, out string mapped)
    {
        mapped = string.Empty;

        var output = new StringBuilder(input.Length);

        foreach (var rune in input.EnumerateRunes())
        {
            if (!TryAppend(rune, output))
                return false;
        }

        var result = output.ToString();

        mapped = result.ToFormC();

        return true;
    }

    private static bool TryAppend(Rune rune, StringBuilder output)
    {
        if (rune.IsAscii)
            return TryAppendAscii((char)rune.Value, output);

        if (!IdnaMappingTable.Instance.TryGetMapping(rune.Value, out var mapping))
            return false;

        switch (mapping!.Status)
        {
            case IdnaStatus.Valid:
            case IdnaStatus.Deviation:
                output.Append(rune.ToString());

                return true;
            case IdnaStatus.Mapped:
                // A mapping can land on ASCII (the full-width dot becomes a dot), so it goes through the same check as
                // typed ASCII.
                return mapping.Replacement.EnumerateRunes().All(target => TryAppendMapped(target, output));
            default:
                return true;
        }
    }

    private static bool TryAppendMapped(Rune rune, StringBuilder output)
    {
        if (rune.IsAscii)
            return TryAppendAscii((char)rune.Value, output);

        output.Append(rune.ToString());

        return true;
    }

    private static bool TryAppendAscii(char c, StringBuilder output)
    {
        // STD3 rules: the only ASCII a name may hold is letters, digits, hyphen and the dot between labels. Upper case
        // is what the mapping table lower-cases.
        if (c is >= 'A' and <= 'Z')
            c = (char)(c + ('a' - 'A'));
        else if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.'))
            return false;

        output.Append(c);

        return true;
    }
}
