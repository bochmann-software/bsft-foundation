namespace Bsft.Scheduling;

/// <summary>
/// The time-of-day half of a cron expression: the hour and minute fields.
/// </summary>
internal sealed class CronTimeMatcher
{
    private readonly CronField _hours;
    private readonly CronField _minutes;

    /// <summary>
    /// Initializes a new instance of the <see cref="CronTimeMatcher"/> class.
    /// </summary>
    /// <param name="hours">The parsed hour field.</param>
    /// <param name="minutes">The parsed minute field.</param>
    public CronTimeMatcher(CronField hours, CronField minutes)
    {
        _hours = hours;
        _minutes = minutes;
    }

    /// <summary>Determines whether the hour field selects the given hour.</summary>
    /// <param name="hour">The hour of the day.</param>
    /// <returns><c>true</c> when the hour is selected.</returns>
    public bool IsHourAllowed(int hour) => _hours.Contains(hour);

    /// <summary>Determines whether the minute field selects the given minute.</summary>
    /// <param name="minute">The minute of the hour.</param>
    /// <returns><c>true</c> when the minute is selected.</returns>
    public bool IsMinuteAllowed(int minute) => _minutes.Contains(minute);
}
