namespace Bsft.Core;

/// <summary>Decodes Punycode (RFC 3492 section 6.2).</summary>
internal sealed class PunycodeDecoder
{
    private const int MaxCodePoint = 0x10FFFF;

    private readonly string _encoded;
    private readonly List<int> _codePoints = [];

    private int _position;

    private PunycodeDecoder(string encoded) => _encoded = encoded;

    /// <summary>Decodes a label.</summary>
    /// <param name="encoded">The encoded form, without the <c>xn--</c> prefix; ASCII and lower case, as a mapped name has it.</param>
    /// <param name="label">The Unicode label when decoding succeeds.</param>
    /// <returns><see langword="false"/> when the input is not valid Punycode.</returns>
    public static bool TryDecode(string encoded, out string label)
    {
        var decoder = new PunycodeDecoder(encoded);

        if (!decoder.TryRun())
        {
            label = string.Empty;

            return false;
        }

        label = string.Concat(decoder._codePoints.Select(codePoint => char.ConvertFromUtf32(codePoint)));

        return true;
    }

    private bool TryRun()
    {
        ReadBasic();

        var n = (long)PunycodeParameters.InitialN;
        var i = 0L;
        var bias = PunycodeParameters.InitialBias;

        while (_position < _encoded.Length)
        {
            var previous = i;

            if (!TryReadDelta(bias, ref i))
                return false;

            var points = _codePoints.Count + 1;

            bias = previous == 0
                ? PunycodeParameters.AdaptFirst(i - previous, points)
                : PunycodeParameters.AdaptNext(i - previous, points);
            n += i / points;
            i %= points;

            if (n > MaxCodePoint || !System.Text.Rune.IsValid((int)n))
                return false;

            _codePoints.Insert((int)i, (int)n);
            i++;
        }

        return true;
    }

    private void ReadBasic()
    {
        var delimiter = _encoded.LastIndexOf(PunycodeParameters.Delimiter);

        if (delimiter <= 0)
            return;

        _codePoints.AddRange(_encoded.Take(delimiter).Select(c => (int)c));
        _position = delimiter + 1;
    }

    private bool TryReadDelta(int bias, ref long i)
    {
        var weight = 1L;

        for (var k = PunycodeParameters.Base; ; k += PunycodeParameters.Base)
        {
            if (_position >= _encoded.Length || DigitValue(_encoded[_position++]) is not { } digit)
                return false;

            i += digit * weight;

            var threshold = PunycodeParameters.Threshold(k, bias);

            if (digit < threshold)
                return true;

            weight *= PunycodeParameters.Base - threshold;

            if (weight > int.MaxValue || i > int.MaxValue)
                return false;
        }
    }

    private static int? DigitValue(char c) => c switch
    {
        >= 'a' and <= 'z' => c - 'a',
        >= '0' and <= '9' => c - '0' + PunycodeParameters.Letters,
        _ => null,
    };
}
