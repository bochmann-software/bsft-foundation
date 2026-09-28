namespace Bsft.Scheduling;

/// <summary>
/// A parsed five-field cron expression — <c>minute hour day-of-month month day-of-week</c> — that can
/// compute the next instant it matches.
/// </summary>
/// <remarks>
/// <para>
/// Each field accepts <c>*</c>, a value, a <c>a-b</c> range, a <c>,</c> list and a <c>/n</c> step;
/// months accept <c>JAN</c>–<c>DEC</c> and days of the week <c>SUN</c>–<c>SAT</c>, case-insensitively.
/// Day of week accepts both <c>0</c> and <c>7</c> for Sunday. Wrap-around ranges such as
/// <c>22-4</c> are rejected rather than guessed at; write <c>22-23,0-4</c> instead.
/// </para>
/// <para>
/// This is deliberately a small self-contained parser rather than a dependency: the framework ships
/// no third-party scheduling package, and the five standard fields cover what a recurring job needs.
/// The non-standard extensions of larger libraries — <c>L</c>, <c>W</c>, <c>#</c>, seconds — are not
/// supported and are reported as a parse error instead of being silently ignored.
/// </para>
/// <para>
/// All matching happens in UTC. A cron expression therefore does not drift across a daylight saving
/// transition, at the price of a local-time job moving by an hour twice a year.
/// </para>
/// </remarks>
public sealed class CronExpression
{
    private const int FieldCount = 5;
    private const int MinutesPerHour = 60;

    /// <summary>
    /// The search horizon in days. A valid expression that never matches — <c>0 0 30 2 *</c>, the
    /// thirtieth of February — has to terminate rather than spin, and five years is far beyond any
    /// legitimate recurrence gap (the widest is 29 February, four years apart).
    /// </summary>
    private const int MaxSearchDays = 5 * 366;

    private static readonly CronFieldRange MinuteRange = new("minute", 0, 59, null);
    private static readonly CronFieldRange HourRange = new("hour", 0, 23, null);
    private static readonly CronFieldRange DayOfMonthRange = new("day-of-month", 1, 31, null);

    private static readonly CronFieldRange MonthRange = new(
        "month", 1, 12, BuildNames(["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"], 1));

    private static readonly CronFieldRange DayOfWeekRange = new(
        "day-of-week", 0, 7, BuildNames(["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"], 0));

    private readonly CronDateMatcher _date;
    private readonly CronTimeMatcher _time;

    private CronExpression(string expression, CronDateMatcher date, CronTimeMatcher time)
    {
        Expression = expression;
        _date = date;
        _time = time;
    }

    /// <summary>Gets the original expression text.</summary>
    public string Expression { get; }

    /// <summary>
    /// Parses a five-field cron expression.
    /// </summary>
    /// <param name="expression">The expression text.</param>
    /// <returns>The parsed expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="expression"/> is null.</exception>
    /// <exception cref="CronFormatException">Thrown when the expression is not a valid five-field cron expression.</exception>
    public static CronExpression Parse(string expression)
    {
        if (expression is null)
            throw new ArgumentNullException(nameof(expression));

        var fields = expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != FieldCount)
        {
            throw new CronFormatException(
                $"A cron expression must have exactly {FieldCount} fields (minute hour day-of-month month day-of-week), but '{expression}' has {fields.Length}.");
        }

        var date = new CronDateMatcher(
            CronField.Parse(fields[3].ToUpperInvariant(), MonthRange),
            CronField.Parse(fields[2], DayOfMonthRange),
            CronField.Parse(fields[4].ToUpperInvariant(), DayOfWeekRange));

        var time = new CronTimeMatcher(
            CronField.Parse(fields[1], HourRange),
            CronField.Parse(fields[0], MinuteRange));

        return new CronExpression(expression, date, time);
    }

    /// <summary>
    /// Attempts to parse a five-field cron expression without throwing.
    /// </summary>
    /// <param name="expression">The expression text.</param>
    /// <param name="result">The parsed expression when parsing succeeded; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> when the expression was parsed.</returns>
    public static bool TryParse(string expression, out CronExpression? result)
    {
        try
        {
            result = Parse(expression);
            return true;
        }
        catch (CronFormatException)
        {
            // The caller asked for a boolean answer, so a malformed expression is the expected
            // outcome here rather than a failure. A null argument still throws.
            result = null;
            return false;
        }
    }

    /// <summary>
    /// Computes the first instant strictly after <paramref name="after"/> at which this expression
    /// matches.
    /// </summary>
    /// <param name="after">The instant to search from, exclusive.</param>
    /// <returns>The next matching instant in UTC, or <c>null</c> when the expression can never match.</returns>
    public DateTimeOffset? FindNextOccurrence(DateTimeOffset after)
    {
        // Truncate to the minute and step one forward, so the result is always strictly later than
        // the input and a job that just ran cannot immediately match its own occurrence again.
        var utc = after.UtcDateTime;
        var candidate = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc)
            .AddMinutes(1);

        var horizon = candidate.AddDays(MaxSearchDays);

        // Coarse fields are advanced whole units at a time — an unmatched month jumps to the first of
        // the next month rather than walking 40 000 minutes — so the loop converges in a few hundred
        // steps even for a yearly schedule.
        while (candidate < horizon)
        {
            if (!_date.IsMonthAllowed(candidate.Month))
            {
                candidate = new DateTime(candidate.Year, candidate.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                continue;
            }

            if (!_date.IsDayAllowed(candidate))
            {
                candidate = candidate.Date.AddDays(1);
                continue;
            }

            if (!_time.IsHourAllowed(candidate.Hour))
            {
                candidate = candidate.Date.AddMinutes((candidate.Hour + 1) * MinutesPerHour);
                continue;
            }

            if (!_time.IsMinuteAllowed(candidate.Minute))
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            return new DateTimeOffset(candidate);
        }

        return null;
    }

    private static IReadOnlyDictionary<string, int> BuildNames(string[] names, int offset)
    {
        var map = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
        {
            map[names[i]] = i + offset;
        }

        return map;
    }

    /// <summary>Returns the original expression text.</summary>
    /// <returns>The expression text.</returns>
    public override string ToString() => Expression;
}
