using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests;

public class CronFieldTests
{
    private static readonly CronFieldRange _dayOfMonth = new("day-of-month", 1, 31, null);

    [Fact]
    public void Contains_WhenValueIsInsideTheParsedTerms_ReturnsTrue()
    {
        var field = CronField.Parse("10-20", _dayOfMonth);

        field.Contains(10).Should().BeTrue();
        field.Contains(20).Should().BeTrue();
    }

    [Fact]
    public void Contains_WhenValueIsBelowTheRange_ReturnsFalse()
    {
        var field = CronField.Parse("*", _dayOfMonth);

        field.Contains(0).Should().BeFalse();
    }

    [Fact]
    public void Contains_WhenValueIsAboveTheRange_ReturnsFalse()
    {
        var field = CronField.Parse("*", _dayOfMonth);

        field.Contains(32).Should().BeFalse();
    }
}
