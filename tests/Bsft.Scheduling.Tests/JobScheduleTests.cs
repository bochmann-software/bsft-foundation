using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests;

public class JobScheduleTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void AnIntervalScheduleAddsItsIntervalToTheAnchor()
    {
        var schedule = JobSchedule.Every(TimeSpan.FromMinutes(15));

        schedule.FindNextOccurrence(_now).Should().Be(_now.AddMinutes(15));
        schedule.Interval.Should().Be(TimeSpan.FromMinutes(15));
        schedule.CronExpressionText.Should().BeNull();
    }

    [Fact]
    public void ACronScheduleDelegatesToTheExpression()
    {
        var schedule = JobSchedule.Cron("0 3 * * *");

        schedule.FindNextOccurrence(_now).Should().Be(new DateTimeOffset(2026, 8, 4, 3, 0, 0, TimeSpan.Zero));
        schedule.CronExpressionText.Should().Be("0 3 * * *");
        schedule.Interval.Should().BeNull();
    }

    [Fact]
    public void ACronScheduleWithNoFurtherOccurrenceReturnsNull()
    {
        JobSchedule.Cron("0 0 30 2 *").FindNextOccurrence(_now).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveIntervalIsRejected(int minutes)
    {
        // A zero interval would make every occurrence due the moment it is created, starving every
        // other job.
        var act = () => JobSchedule.Every(TimeSpan.FromMinutes(minutes));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("interval");
    }

    [Fact]
    public void AnInvalidCronExpressionIsRejectedAtConstruction()
    {
        var act = () => JobSchedule.Cron("not a cron");

        act.Should().Throw<CronFormatException>();
    }

    [Fact]
    public void ANullCronExpressionIsRejected()
    {
        var act = () => JobSchedule.Cron(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("expression");
    }

    [Fact]
    public void TheDescriptionNamesTheRule()
    {
        JobSchedule.Cron("0 3 * * *").ToString().Should().Be("0 3 * * *");
        JobSchedule.Every(TimeSpan.FromMinutes(5)).ToString().Should().Contain("every");
    }
}
