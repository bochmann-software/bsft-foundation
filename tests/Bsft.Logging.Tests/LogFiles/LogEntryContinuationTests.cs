using Bsft.Logging.LogFiles;
using FluentAssertions;

namespace Bsft.Logging.Tests.LogFiles;

public sealed class LogEntryContinuationTests
{
    [Fact]
    public void Add_WhenInputRunsFarBeyondItemCap_EvictsWithoutRepeatedLinearShifts()
    {
        const int maximumBytes = 1024 * 1024;
        const int inputCount = 1_000_000;
        var continuation = new LogEntryContinuation(maximumBytes);

        for (var index = 0; index < inputCount; index++)
            continuation.Add(new PhysicalLogInput(index, index.ToString(), 0, IsTruncated: false));

        continuation.Lines.Should().HaveCount(maximumBytes / 32);
        continuation.Lines.First().Offset.Should().Be(inputCount - (maximumBytes / 32));
        continuation.Lines.Last().Offset.Should().Be(inputCount - 1);
        continuation.WasTruncated.Should().BeTrue();
    }
}
