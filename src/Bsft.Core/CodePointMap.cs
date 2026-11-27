namespace Bsft.Core;

/// <summary>An immutable map from Unicode code points to values, stored as sorted, non-overlapping ranges.</summary>
/// <typeparam name="TValue">The value a range carries.</typeparam>
internal sealed class CodePointMap<TValue>
{
    private readonly int[] _first;
    private readonly int[] _last;
    private readonly TValue[] _values;

    /// <summary>Initializes a new instance of the <see cref="CodePointMap{TValue}"/> class.</summary>
    /// <param name="ranges">The ranges, in ascending order and without overlap.</param>
    public CodePointMap(IReadOnlyList<(int First, int Last, TValue Value)> ranges)
    {
        _first = ranges.Select(range => range.First).ToArray();
        _last = ranges.Select(range => range.Last).ToArray();
        _values = ranges.Select(range => range.Value).ToArray();
    }

    /// <summary>Looks up the value of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="value">The value of the range holding <paramref name="codePoint"/>; otherwise the default.</param>
    /// <returns><see langword="true"/> when a range holds the code point.</returns>
    public bool TryGetValue(int codePoint, out TValue? value)
    {
        var index = Array.BinarySearch(_first, codePoint);

        if (index < 0)
            index = ~index - 1;

        if (index >= 0 && codePoint <= _last[index])
        {
            value = _values[index];

            return true;
        }

        value = default;

        return false;
    }

    /// <summary>Tells whether a range holds a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> when a range holds it.</returns>
    public bool Contains(int codePoint) => TryGetValue(codePoint, out _);
}
