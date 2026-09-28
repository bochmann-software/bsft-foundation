using FluentAssertions;
using Xunit;

namespace Bsft.Calendar.Tests;

public class BusinessCalendarTests
{
    private static readonly DateTimeOffset _fridayNoon = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Zones are built here rather than read from the operating system: the installed time-zone
    /// database differs per machine and per container image, and a test about working days must not
    /// depend on it.
    /// </summary>
    private static TimeZoneInfo BerlinSummer => TimeZoneInfo.CreateCustomTimeZone(
        "bsft-tests-berlin-summer", TimeSpan.FromHours(2), "Berlin (summer)", "Berlin (summer)");

    /// <summary>A zone whose clocks go forward on 25 March and back on 30 October, both weekdays in 2026.</summary>
    private static TimeZoneInfo TransitioningZone
    {
        get
        {
            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date,
                DateTime.MaxValue.Date,
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 25),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 30));

            return TimeZoneInfo.CreateCustomTimeZone(
                "bsft-tests-transitioning", TimeSpan.FromHours(1), "Test CET", "Test CET", "Test CEST", [rule]);
        }
    }

    [Fact]
    public void ContinuousCalendar_TreatsEveryDayAsBusinessDay()
    {
        var calendar = ContinuousBusinessCalendar.Instance;

        calendar.IsBusinessDay(new DateOnly(2026, 8, 8)).Should().BeTrue();
        calendar.IsBusinessDay(new DateOnly(2026, 8, 9)).Should().BeTrue();
    }

    [Fact]
    public void ContinuousCalendar_AddsTheDelayVerbatim()
    {
        var result = ContinuousBusinessCalendar.Instance.Add(_fridayNoon, TimeSpan.FromDays(2));

        result.Should().Be(_fridayNoon.AddDays(2));
    }

    [Fact]
    public void ContinuousCalendar_IgnoresNonPositiveDelays()
    {
        ContinuousBusinessCalendar.Instance.Add(_fridayNoon, TimeSpan.Zero).Should().Be(_fridayNoon);
        ContinuousBusinessCalendar.Instance.Add(_fridayNoon, TimeSpan.FromHours(-3)).Should().Be(_fridayNoon);
    }

    [Fact]
    public void WeekdayCalendar_SkipsTheWeekendByDefault()
    {
        var calendar = new WeekdayBusinessCalendar();

        calendar.IsBusinessDay(new DateOnly(2026, 8, 7)).Should().BeTrue();   // Friday
        calendar.IsBusinessDay(new DateOnly(2026, 8, 8)).Should().BeFalse();  // Saturday
        calendar.IsBusinessDay(new DateOnly(2026, 8, 9)).Should().BeFalse();  // Sunday
    }

    [Fact]
    public void WeekdayCalendar_QuoteSentOnFriday_IsChasedOnTuesday()
    {
        // Two business days from Friday noon: the rest of Friday plus Monday plus half of Tuesday.
        var result = new WeekdayBusinessCalendar().Add(_fridayNoon, TimeSpan.FromDays(2));

        result.Should().Be(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
        result.DayOfWeek.Should().Be(DayOfWeek.Tuesday);
    }

    [Fact]
    public void WeekdayCalendar_DelayThatFitsInTheSameDay_StaysOnThatDay()
    {
        var result = new WeekdayBusinessCalendar().Add(_fridayNoon, TimeSpan.FromHours(3));

        result.Should().Be(_fridayNoon.AddHours(3));
    }

    [Fact]
    public void WeekdayCalendar_StartingOnANonBusinessDay_ConsumesNoTimeThere()
    {
        var saturdayNoon = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

        var result = new WeekdayBusinessCalendar().Add(saturdayNoon, TimeSpan.FromHours(6));

        // The weekend contributes nothing, so the delay starts accruing at Monday midnight.
        result.Should().Be(new DateTimeOffset(2026, 8, 10, 6, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WeekdayCalendar_WithOneWorkingDayAWeek_SkipsTheWholeRunBetweenThem()
    {
        // Six non-working days in a row is the sparsest week the constructor allows, and the run is
        // stepped over at once rather than a day at a time. Friday noon plus six business hours lands
        // on the following Wednesday at six, because nothing between them counts.
        var calendar = new WeekdayBusinessCalendar(
        [
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
            DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday
        ]);

        var result = calendar.Add(_fridayNoon, TimeSpan.FromHours(6));

        result.Should().Be(new DateTimeOffset(2026, 8, 12, 6, 0, 0, TimeSpan.Zero));
        result.DayOfWeek.Should().Be(DayOfWeek.Wednesday);
    }

    [Fact]
    public void WeekdayCalendar_IgnoresNonPositiveDelays()
    {
        new WeekdayBusinessCalendar().Add(_fridayNoon, TimeSpan.Zero).Should().Be(_fridayNoon);
    }

    [Fact]
    public void WeekdayCalendar_AcceptsACustomSetOfNonWorkingDays()
    {
        var calendar = new WeekdayBusinessCalendar([DayOfWeek.Sunday]);

        calendar.IsBusinessDay(new DateOnly(2026, 8, 8)).Should().BeTrue();
        calendar.Add(_fridayNoon, TimeSpan.FromDays(2)).Should().Be(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WeekdayCalendar_RejectsAnAbsurdDelay()
    {
        // The calculation walks day by day, so an unbounded delay coming from business data would
        // burn CPU proportional to its length.
        var act = () => new WeekdayBusinessCalendar().Add(_fridayNoon, TimeSpan.FromDays(4000));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("delay");
    }

    [Fact]
    public void WeekdayCalendar_AcceptsADelayAtTheLimit()
    {
        var act = () => new WeekdayBusinessCalendar().Add(_fridayNoon, TimeSpan.FromDays(3650));

        act.Should().NotThrow();
    }

    [Fact]
    public void WeekdayCalendar_ReadsTheWorkingDayInItsOwnZoneRatherThanInTheOffsetItIsHanded()
    {
        // Berlin in summer, and the framework's own callers hand the calendar a UTC instant: Monday
        // 00:30 local is Sunday 22:30 UTC. Delimited by UTC midnight the reminder counts as scheduled
        // on the weekend, starts accruing at Monday 02:00 local and fires an hour and a half late —
        // and the skew repeats at every weekend boundary.
        var sundayEveningUtc = new DateTimeOffset(2026, 8, 9, 22, 30, 0, TimeSpan.Zero);
        var calendar = new WeekdayBusinessCalendar(null, BerlinSummer);

        var result = calendar.Add(sundayEveningUtc, TimeSpan.FromDays(1));

        // Tuesday 00:30 Berlin, one working day after Monday 00:30 Berlin.
        result.Should().Be(new DateTimeOffset(2026, 8, 10, 22, 30, 0, TimeSpan.Zero));
        result.Offset.Should().Be(TimeSpan.Zero, "the result is handed back in the offset it came in on");
    }

    [Fact]
    public void WeekdayCalendar_WithoutAZone_DelimitsTheDayByUtcMidnight()
    {
        // The zone is opt-in: a calendar constructed the way 1.3.0 always allowed keeps behaving
        // exactly as before.
        var sundayEveningUtc = new DateTimeOffset(2026, 8, 9, 22, 30, 0, TimeSpan.Zero);

        var result = new WeekdayBusinessCalendar().Add(sundayEveningUtc, TimeSpan.FromDays(1));

        result.Should().Be(new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WeekdayCalendar_CountsADaylightSavingDayAtItsRealLength()
    {
        // The Friday the clocks go back is twenty-five hours long, so twenty-five hours of business
        // time still fit inside it. Carrying the offset of the starting instant across the boundary
        // would end the working day an hour early, and the leftover hour would have to wait out the
        // whole weekend before it could accrue.
        var fridayMidnight = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.FromHours(2));
        var calendar = new WeekdayBusinessCalendar(null, TransitioningZone);

        var result = calendar.Add(fridayMidnight, TimeSpan.FromHours(25));

        result.Should().Be(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void WeekdayCalendar_RejectsAWeekWithoutAnyWorkingDayWhenAZoneIsGiven()
    {
        var act = () => new WeekdayBusinessCalendar(Enum.GetValues<DayOfWeek>(), BerlinSummer);

        act.Should().Throw<ArgumentException>().WithParameterName("nonWorkingDays");
    }

    [Fact]
    public void WeekdayCalendar_RejectsAWeekWithoutAnyWorkingDay()
    {
        var everyDay = Enum.GetValues<DayOfWeek>();

        var act = () => new WeekdayBusinessCalendar(everyDay);

        act.Should().Throw<ArgumentException>().WithParameterName("nonWorkingDays");
    }
}
