namespace Bsft.Core;

/// <summary>The constants and the bias adaptation of Punycode (RFC 3492 section 5 and 6.1).</summary>
internal static class PunycodeParameters
{
    /// <summary>The number of digits.</summary>
    public const int Base = 36;

    /// <summary>The smallest threshold.</summary>
    public const int ThresholdMin = 1;

    /// <summary>The largest threshold.</summary>
    public const int ThresholdMax = 26;

    /// <summary>The bias before the first code point.</summary>
    public const int InitialBias = 72;

    /// <summary>The first code point that is not basic.</summary>
    public const int InitialN = 0x80;

    /// <summary>The separator between the basic and the extended part.</summary>
    public const char Delimiter = '-';

    /// <summary>The number of letters, which the digits <c>a</c> to <c>z</c> stand for before the decimal digits.</summary>
    public const int Letters = 26;

    private const int Skew = 38;
    private const int Damp = 700;

    /// <summary>Gets the threshold for a digit position.</summary>
    /// <param name="k">The position, a multiple of <see cref="Base"/>.</param>
    /// <param name="bias">The current bias.</param>
    /// <returns>The threshold.</returns>
    public static int Threshold(int k, int bias)
    {
        if (k <= bias)
            return ThresholdMin;

        return k >= bias + ThresholdMax ? ThresholdMax : k - bias;
    }

    /// <summary>Adapts the bias after the first code point that is not basic was handled.</summary>
    /// <param name="delta">The delta just encoded.</param>
    /// <param name="points">The number of code points handled, including this one.</param>
    /// <returns>The new bias.</returns>
    public static int AdaptFirst(long delta, int points) => Adapt(delta / Damp, points);

    /// <summary>Adapts the bias after any later code point was handled.</summary>
    /// <param name="delta">The delta just encoded.</param>
    /// <param name="points">The number of code points handled, including this one.</param>
    /// <returns>The new bias.</returns>
    public static int AdaptNext(long delta, int points) => Adapt(delta / 2, points);

    private static int Adapt(long scaledDelta, int points)
    {
        var delta = scaledDelta + (scaledDelta / points);
        var k = 0;

        while (delta > ((Base - ThresholdMin) * ThresholdMax) / 2)
        {
            delta /= Base - ThresholdMin;
            k += Base;
        }

        return (int)(k + (((Base - ThresholdMin + 1) * delta) / (delta + Skew)));
    }
}
