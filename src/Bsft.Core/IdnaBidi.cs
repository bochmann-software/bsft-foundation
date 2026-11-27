
namespace Bsft.Core;

/// <summary>The bidirectional rule of RFC 5893 that UTS 46 applies to a name containing right-to-left text.</summary>
internal static class IdnaBidi
{
    /// <summary>Checks the labels of a name.</summary>
    /// <param name="labels">The Unicode form of every label.</param>
    /// <returns>
    /// <see langword="true"/> when the name is not a bidi name (no label holds R, AL or AN text) or every label meets
    /// the six conditions of RFC 5893 section 2.
    /// </returns>
    public static bool IsSatisfied(IReadOnlyList<string> labels)
    {
        if (labels.All(label => label.All(char.IsAscii)))
            return true;

        var classes = labels.Select(Classify).ToList();

        return !classes.Any(label => label.Any(IsRightToLeftEvidence)) || classes.TrueForAll(IsLabelValid);
    }

    private static List<BidiClass> Classify(string label)
        => label.EnumerateRunes().Select(rune => IdnaBidiTable.Instance.GetClass(rune.Value)).ToList();

    private static bool IsRightToLeftEvidence(BidiClass bidiClass) => bidiClass is BidiClass.R or BidiClass.AL or BidiClass.AN;

    private static bool IsLabelValid(List<BidiClass> label)
    {
        var isRightToLeft = label[0] is BidiClass.R or BidiClass.AL;

        if (!isRightToLeft && label[0] != BidiClass.L)
            return false;

        var end = label.Count - 1;

        while (end > 0 && label[end] == BidiClass.NSM)
            end--;

        return isRightToLeft
            ? label.TrueForAll(IsAllowedRightToLeft)
                && label[end] is BidiClass.R or BidiClass.AL or BidiClass.EN or BidiClass.AN
                && !(label.Contains(BidiClass.EN) && label.Contains(BidiClass.AN))
            : label.TrueForAll(IsAllowedLeftToRight) && label[end] is BidiClass.L or BidiClass.EN;
    }

    private static bool IsAllowedRightToLeft(BidiClass bidiClass)
        => bidiClass is BidiClass.R or BidiClass.AL or BidiClass.AN or BidiClass.EN or BidiClass.ES or BidiClass.CS
            or BidiClass.ET or BidiClass.ON or BidiClass.BN or BidiClass.NSM;

    private static bool IsAllowedLeftToRight(BidiClass bidiClass)
        => bidiClass is BidiClass.L or BidiClass.EN or BidiClass.ES or BidiClass.CS or BidiClass.ET or BidiClass.ON
            or BidiClass.BN or BidiClass.NSM;
}
