namespace Bsft.Scheduling;

/// <summary>
/// The date half of a cron expression: the month, day-of-month and day-of-week fields, which
/// together decide whether a given calendar day is selected.
/// </summary>
/// <remarks>
/// The two day fields are one decision, not two: standard cron matches a day when <em>either</em>
/// selects it, unless one of them is <c>*</c>, in which case the other alone decides. Keeping that
/// rule here is what stops <see cref="CronExpression"/> from having to know about it.
/// </remarks>
internal sealed class CronDateMatcher
{
    /// <summary>Sunday may be written as 0 or as 7, and both spellings must select the same day.</summary>
    private const int SundayAlias = 7;

    private readonly CronField _months;
    private readonly CronField _daysOfMonth;
    private readonly CronField _daysOfWeek;

    /// <summary>
    /// Initializes a new instance of the <see cref="CronDateMatcher"/> class.
    /// </summary>
    /// <param name="months">The parsed month field.</param>
    /// <param name="daysOfMonth">The parsed day-of-month field.</param>
    /// <param name="daysOfWeek">The parsed day-of-week field.</param>
    public CronDateMatcher(CronField months, CronField daysOfMonth, CronField daysOfWeek)
    {
        _months = months;
        _daysOfMonth = daysOfMonth;
        _daysOfWeek = daysOfWeek;
    }

    /// <summary>Determines whether the month field selects the given month.</summary>
    /// <param name="month">The one-based month number.</param>
    /// <returns><c>true</c> when the month is selected.</returns>
    public bool IsMonthAllowed(int month) => _months.Contains(month);

    /// <summary>Determines whether the two day fields together select the given date.</summary>
    /// <param name="candidate">The date to test.</param>
    /// <returns><c>true</c> when the date is selected.</returns>
    public bool IsDayAllowed(DateTime candidate)
    {
        var dayOfWeek = (int)candidate.DayOfWeek;

        var weekMatches = _daysOfWeek.Contains(dayOfWeek)
            || (dayOfWeek == 0 && _daysOfWeek.Contains(SundayAlias));
        var monthDayMatches = _daysOfMonth.Contains(candidate.Day);

        if (_daysOfMonth.IsUnrestricted)
            return weekMatches;

        if (_daysOfWeek.IsUnrestricted)
            return monthDayMatches;

        return monthDayMatches || weekMatches;
    }
}
