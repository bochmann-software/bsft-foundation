using System.Text;

namespace Bsft.Core;

/// <summary>The per-label steps of UTS 46 processing: Punycode, the validity criteria and the joiner rules.</summary>
internal static class IdnaLabel
{
    /// <summary>The prefix of an A-label.</summary>
    public const string AcePrefix = "xn--";

    private const int MaximumLength = 63;
    private const int HyphenPositionThird = 2;
    private const int HyphenPositionFourth = 3;
    private const int ZeroWidthNonJoiner = 0x200C;
    private const int ZeroWidthJoiner = 0x200D;

    /// <summary>Validates one label of a mapped name and converts it.</summary>
    /// <param name="label">The label, as the mapping step left it.</param>
    /// <param name="unicode">The label in its Unicode form.</param>
    /// <param name="ascii">The label in its A-label (or plain ASCII) form.</param>
    /// <returns><see langword="false"/> when the label is not valid under non-transitional processing.</returns>
    public static bool TryConvert(string label, out string unicode, out string ascii)
    {
        unicode = label;
        ascii = label;

        if (label.Length == 0)
            return false;

        var isEncoded = label.StartsWith(AcePrefix, StringComparison.Ordinal);

        if (isEncoded)
        {
            // The rest of an A-label is ASCII by construction; decoding it must give something that needed encoding, and
            // encoding that again must give the label back, so that one name has one A-label and nobody can register
            // the same Unicode label under two spellings.
            if (!IsAscii(label)
                || !PunycodeDecoder.TryDecode(label[AcePrefix.Length..], out unicode)
                || unicode.Length == 0
                || IsAscii(unicode))
            {
                return false;
            }
        }

        if (!IsValid(unicode))
            return false;

        ascii = IsAscii(unicode) ? unicode : AcePrefix + PunycodeEncoder.Encode(unicode);

        return ascii.Length <= MaximumLength && (!isEncoded || string.Equals(ascii, label, StringComparison.Ordinal));
    }

    /// <summary>Checks the validity criteria of UTS 46 for non-transitional processing with hyphen, joiner and STD3 checks.</summary>
    /// <param name="label">The Unicode form of the label.</param>
    /// <returns><see langword="true"/> when the label meets them (the bidi rules apply to the whole name and are checked separately).</returns>
    private static bool IsValid(string label)
    {
        if (!label.IsFormC() || label[0] == '-' || label[^1] == '-')
            return false;

        var runes = label.EnumerateRunes().ToArray();

        // Positions are code points, not UTF-16 units: a supplementary character takes two units.
        if (runes.Length > HyphenPositionFourth && runes[HyphenPositionThird].Value == '-' && runes[HyphenPositionFourth].Value == '-')
            return false;

        return !IdnaMarkTable.Instance.IsMark(runes[0].Value) && runes.All(IsValidRune) && AreJoinersValid(runes);
    }

    private static bool IsValidRune(Rune rune)
    {
        if (rune.IsAscii)
            return rune.Value is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-';

        return IdnaMappingTable.Instance.TryGetMapping(rune.Value, out var mapping)
            && mapping!.Status is IdnaStatus.Valid or IdnaStatus.Deviation;
    }

    private static bool AreJoinersValid(Rune[] runes)
    {
        for (var i = 0; i < runes.Length; i++)
        {
            var isValid = runes[i].Value switch
            {
                ZeroWidthJoiner => i > 0 && IdnaJoiningTable.Instance.IsVirama(runes[i - 1].Value),
                ZeroWidthNonJoiner => (i > 0 && IdnaJoiningTable.Instance.IsVirama(runes[i - 1].Value)) || IsJoiningContext(runes, i),
                _ => true,
            };

            if (!isValid)
                return false;
        }

        return true;
    }

    // (Joining_Type:{L,D}) (Joining_Type:T)* ZWNJ (Joining_Type:T)* (Joining_Type:{R,D}), RFC 5892 appendix A.1.
    private static bool IsJoiningContext(Rune[] runes, int index)
    {
        var before = index - 1;

        while (before >= 0 && IdnaJoiningTable.Instance.GetJoiningType(runes[before].Value) == 'T')
            before--;

        var after = index + 1;

        while (after < runes.Length && IdnaJoiningTable.Instance.GetJoiningType(runes[after].Value) == 'T')
            after++;

        return before >= 0
            && after < runes.Length
            && IdnaJoiningTable.Instance.GetJoiningType(runes[before].Value) is 'L' or 'D'
            && IdnaJoiningTable.Instance.GetJoiningType(runes[after].Value) is 'R' or 'D';
    }

    private static bool IsAscii(string text) => text.All(char.IsAscii);
}
