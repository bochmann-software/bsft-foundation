using System.Text;
using Bsft.Logging.LogFiles;
using FluentAssertions;

namespace Bsft.Logging.Tests.LogFiles;

public sealed class Utf8BoundaryTests
{
    [Fact]
    public void CompleteRange_WhenBothEdgesSplitMultibyteCharacters_ReturnsOnlyCompleteUtf8()
    {
        var encoded = Encoding.UTF8.GetBytes("äA😀B");
        var splitAtBothEdges = encoded.AsSpan(1, 5);

        var range = Utf8Boundary.CompleteRange(splitAtBothEdges);

        range.Start.Should().Be(1);
        range.Length.Should().Be(1);
        Encoding.UTF8.GetString(splitAtBothEdges.Slice(range.Start, range.Length)).Should().Be("A");
    }
}
