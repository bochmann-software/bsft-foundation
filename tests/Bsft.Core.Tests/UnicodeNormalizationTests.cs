using System.Globalization;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests;

/// <summary>
/// The normalization forms, checked against the Unicode conformance vectors (a selection of NormalizationTest.txt,
/// 18.0.0) so that the answer does not depend on the host's ICU or on the globalization mode.
/// </summary>
public sealed class UnicodeNormalizationTests
{
    private const string VectorResource = "Bsft.Core.Tests.normalization-vectors.txt";

    [Fact]
    public void Normalize_ForTheConformanceVectors_GivesTheExpectedForms()
    {
        var failures = new List<string>();
        var checkedLines = 0;

        foreach (var line in ReadVectors())
        {
            var columns = line.Split(';').Take(5).Select(Parse).ToArray();

            checkedLines++;

            foreach (var column in new[] { 0, 1, 2 })
                Check(failures, line, "NFC", columns[column].ToFormC(), columns[1]);

            foreach (var column in new[] { 3, 4 })
                Check(failures, line, "NFC", columns[column].ToFormC(), columns[3]);

            foreach (var column in new[] { 0, 1, 2 })
                Check(failures, line, "NFD", columns[column].ToFormD(), columns[2]);

            foreach (var column in new[] { 3, 4 })
                Check(failures, line, "NFD", columns[column].ToFormD(), columns[4]);
        }

        checkedLines.Should().BeGreaterThan(5000);
        failures.Should().BeEmpty();
    }

    [Theory]
    [InlineData("각", "각")]
    [InlineData("가", "가")]
    [InlineData("Å", "Å")]
    [InlineData("Å", "Å")]
    [InlineData("ạ̇", "ạ̇")]
    [InlineData("ạ̇", "ạ̇")]
    public void ToFormD_Decomposes_AndOrdersTheMarks(string text, string expected)
        => text.ToFormD().Should().Be(expected);

    [Theory]
    [InlineData("각", "각")]
    [InlineData("가", "가")]
    [InlineData("Å", "Å")]
    [InlineData("ạ̇", "ạ̇")]
    [InlineData("́a", "́a")]
    [InlineData("á́", "á́")]
    [InlineData("क़", "क़")]
    public void ToFormC_Composes_WhereTheMarkIsNotBlocked(string text, string expected)
        => text.ToFormC().Should().Be(expected);

    [Fact]
    public void Normalize_ForAscii_ReturnsTheSameString()
    {
        const string text = "example.com";

        text.ToFormC().Should().BeSameAs(text);
        text.ToFormD().Should().BeSameAs(text);
    }

    [Theory]
    [InlineData("ü", true)]
    [InlineData("ü", false)]
    [InlineData("abc", true)]
    public void IsFormC_TellsWhetherComposingChangesNothing(string text, bool expected)
        => text.IsFormC().Should().Be(expected);

    [Fact]
    public void RemoveDefaultIgnorables_RemovesJoinersSelectorsAndTheSoftHyphen()
        => "a‌b‍c­d️e͏ㅤf".RemoveDefaultIgnorables().Should().Be("abcdef");

    [Fact]
    public void RemoveDefaultIgnorables_KeepsOtherText_IncludingSupplementaryCharacters()
        => "a\U00020000क".RemoveDefaultIgnorables().Should().Be("a\U00020000क");

    [Fact]
    public void Methods_ForNull_Throw()
    {
        var actions = new Action[]
        {
            () => UnicodeNormalization.ToFormC(null!),
            () => UnicodeNormalization.ToFormD(null!),
            () => UnicodeNormalization.IsFormC(null!),
            () => UnicodeNormalization.RemoveDefaultIgnorables(null!),
        };

        foreach (var action in actions)
            action.Should().Throw<ArgumentNullException>();
    }

    private static void Check(List<string> failures, string line, string form, string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            failures.Add($"{form}: {line}");
    }

    private static string Parse(string column)
        => string.Concat(column.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(code => char.ConvertFromUtf32(int.Parse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));

    private static IEnumerable<string> ReadVectors()
    {
        using var stream = typeof(UnicodeNormalizationTests).Assembly.GetManifestResourceStream(VectorResource)!;
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && line[0] is not ('#' or '@'))
                yield return line;
        }
    }
}
