using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests;

/// <summary>
/// The official UTS 46 vectors (IdnaTestV2.txt, Unicode 18.0.0) against <see cref="InternetDomain.TryCanonicalizeDomain"/>:
/// a name the vectors refuse must be refused, a name they accept must give the A-label they expect. The only accepted names
/// that are refused here are the ones <see cref="DeliberateRefusal"/> names, each of them a rule of the canonical form
/// that goes beyond UTS 46; the test asserts the refusal, so a drift in either direction fails it.
/// </summary>
public sealed partial class IdnaConformanceTests
{
    private const string Resource = "Bsft.Core.Tests.IdnaTestV2.txt";
    private const int FieldCount = 7;

    public static TheoryData<int, string, string?> Cases()
    {
        var data = new TheoryData<int, string, string?>();
        var lineNumber = 0;

        using var stream = typeof(IdnaConformanceTests).Assembly.GetManifestResourceStream(Resource)!;
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;

            var content = line.Split('#', 2)[0];

            if (string.IsNullOrWhiteSpace(content))
                continue;

            var fields = content.Split(';').Select(field => field.Trim()).ToArray();

            // Blank means "as the column before" for the results and "no errors" for a status that follows a blank status
            // of its own, as the header of the file explains.
            // The source stays in its escaped form (it may hold a lone surrogate, which a theory cannot carry) and is
            // unescaped by the test; the A-label of a valid name is plain ASCII.
            var toUnicode = fields[1].Length == 0 ? fields[0] : fields[1];
            var toAscii = fields[3].Length == 0 ? toUnicode : fields[3];
            var asciiStatus = fields[4].Length == 0 ? fields[2] : fields[4];

            fields.Length.Should().BeGreaterThanOrEqualTo(FieldCount - 1);
            data.Add(lineNumber, fields[0], IsError(asciiStatus) ? null : toAscii);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TryCanonicalizeDomain_ForTheVectorsOfUts46_AgreesWithThem(int line, string displayed, string? expected)
    {
        var source = Unescape(displayed);
        var accepted = source.TryCanonicalizeDomain(out var canonical);

        if (expected is null)
        {
            accepted.Should().BeFalse($"line {line} ({displayed}) is invalid");

            return;
        }

        if (DeliberateRefusal(source, expected) is { } reason)
        {
            accepted.Should().BeFalse($"line {line} ({displayed}) is refused on purpose: {reason}");

            return;
        }

        accepted.Should().BeTrue($"line {line} ({displayed}) is valid");
        canonical.Should().Be(expected, $"line {line} ({displayed})");
    }

    [Fact]
    public void Cases_CoverTheWholeFile()
        => Cases().Count.Should().BeGreaterThan(6000);

    /// <summary>The reason a valid name of the vectors is refused by the canonical form on purpose, or <see langword="null"/>.</summary>
    private static string? DeliberateRefusal(string source, string expected)
    {
        var labels = expected.Split('.');

        if (expected.Length == 0)
            return "the empty name is not a domain";

        if (expected.EndsWith('.'))
            return "a trailing dot is refused";

        if (labels.Length < 2)
            return "a domain has at least two labels";

        if (labels[^1].All(char.IsAsciiDigit))
            return "a last label of digits is an address, not a name";

        if (source.Contains('̸', StringComparison.Ordinal) && source.IndexOfAny(['=', '<', '>']) >= 0)
            return "the STD3 check runs before normalization, so a not-equal sign that U+0338 would compose is refused (fail-closed)";

        return null;
    }

    private static bool IsError(string status) => status.Length > 0 && status != "[]";

    private static string Unescape(string text)
    {
        if (text == "\"\"")
            return string.Empty;

        return EscapePattern().Replace(
            text,
            match =>
            {
                var value = int.Parse(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

                // A lone surrogate is kept as it is: the vectors use it to check that it is refused.
                return value is >= 0xD800 and <= 0xDFFF ? ((char)value).ToString() : char.ConvertFromUtf32(value);
            });
    }

    [GeneratedRegex(@"\\u([0-9A-Fa-f]{4})|\\x\{([0-9A-Fa-f]+)\}")]
    private static partial Regex EscapePattern();
}
