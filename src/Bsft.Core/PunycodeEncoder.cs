using System.Text;

namespace Bsft.Core;

/// <summary>Encodes a label as Punycode (RFC 3492 section 6.3).</summary>
internal sealed class PunycodeEncoder
{
    private readonly int[] _codePoints;
    private readonly StringBuilder _output = new();
    private readonly int _basicLength;

    private int _handled;
    private int _n = PunycodeParameters.InitialN;
    private long _delta;
    private int _bias = PunycodeParameters.InitialBias;

    private PunycodeEncoder(string label)
    {
        _codePoints = label.EnumerateRunes().Select(rune => rune.Value).ToArray();

        foreach (var codePoint in _codePoints.Where(codePoint => codePoint < PunycodeParameters.InitialN))
            _output.Append((char)codePoint);

        _basicLength = _output.Length;
        _handled = _basicLength;

        if (_basicLength > 0)
            _output.Append(PunycodeParameters.Delimiter);
    }

    /// <summary>Encodes a label.</summary>
    /// <param name="label">The Unicode label.</param>
    /// <returns>The encoded form, without the <c>xn--</c> prefix.</returns>
    public static string Encode(string label) => new PunycodeEncoder(label).Run();

    private string Run()
    {
        while (_handled < _codePoints.Length)
            EncodeNextCodePoint();

        return _output.ToString();
    }

    private void EncodeNextCodePoint()
    {
        var next = _codePoints.Where(codePoint => codePoint >= _n).Min();

        _delta += (long)(next - _n) * (_handled + 1);
        _n = next;

        foreach (var codePoint in _codePoints)
            Visit(codePoint);

        _delta++;
        _n++;
    }

    private void Visit(int codePoint)
    {
        if (codePoint < _n)
            _delta++;

        if (codePoint != _n)
            return;

        AppendDelta();
        _bias = _handled == _basicLength
            ? PunycodeParameters.AdaptFirst(_delta, _handled + 1)
            : PunycodeParameters.AdaptNext(_delta, _handled + 1);
        _delta = 0;
        _handled++;
    }

    private void AppendDelta()
    {
        var q = _delta;

        for (var k = PunycodeParameters.Base; ; k += PunycodeParameters.Base)
        {
            var threshold = PunycodeParameters.Threshold(k, _bias);

            if (q < threshold)
                break;

            _output.Append(Digit((int)(threshold + ((q - threshold) % (PunycodeParameters.Base - threshold)))));
            q = (q - threshold) / (PunycodeParameters.Base - threshold);
        }

        _output.Append(Digit((int)q));
    }

    private static char Digit(int value)
        => (char)(value < PunycodeParameters.Letters ? 'a' + value : '0' + value - PunycodeParameters.Letters);
}
