using System.Globalization;

namespace Bsft.Core;

/// <summary>Reads the generated Unicode tables embedded in this assembly (see <c>tools/unicode-data/generate.py</c>).</summary>
internal static class IdnaResource
{
    private const char SectionStart = '[';
    private const char CommentStart = '#';

    /// <summary>Reads the lines of a section of a resource, as the ranges they hold.</summary>
    /// <param name="resource">The manifest name of the resource.</param>
    /// <param name="section">The section name including brackets, or <see langword="null"/> for a resource without sections.</param>
    /// <returns>Each data line as its first and last code point and the text after the range.</returns>
    public static IReadOnlyList<(int First, int Last, string Text)> ReadRanges(string resource, string? section)
    {
        using var stream = typeof(IdnaResource).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The embedded resource {resource} is missing.");
        using var reader = new StreamReader(stream);

        var ranges = new List<(int, int, string)>();
        var current = (string?)null;

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == CommentStart)
                continue;

            if (line[0] == SectionStart)
            {
                current = line;

                continue;
            }

            if (current == section)
                ranges.Add(ParseLine(line));
        }

        return ranges;
    }

    /// <summary>Parses a hexadecimal code point or a sequence of them separated by spaces.</summary>
    /// <param name="hex">The text.</param>
    /// <returns>The code points as a string.</returns>
    public static string ParseSequence(string hex)
        => string.Concat(hex.Split(' ').Select(code => char.ConvertFromUtf32(ParseHex(code))));

    private static (int First, int Last, string Text) ParseLine(string line)
    {
        var space = line.IndexOf(' ', StringComparison.Ordinal);
        var span = space < 0 ? line : line[..space];
        var rest = space < 0 ? string.Empty : line[(space + 1)..];
        var dash = span.IndexOf('-', StringComparison.Ordinal);

        return dash < 0
            ? (ParseHex(span), ParseHex(span), rest)
            : (ParseHex(span[..dash]), ParseHex(span[(dash + 1)..]), rest);
    }

    private static int ParseHex(string hex) => int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
