namespace Bsft.Calendar;

/// <summary>
/// A calendar that treats a configurable set of weekdays as non-working and skips them when adding
/// a delay. Public-holiday rules are deliberately left to the application: they differ per country,
/// per federal state and per organisational unit, and hard-coding them in the framework would be wrong.
/// </summary>
public sealed class WeekdayBusinessCalendar : IBusinessCalendar
{
    private const int DaysPerWeek = 7;

    /// <summary>
    /// Upper bound on a single delay. The calculation walks day by day, so an unbounded delay taken
    /// from business data would burn CPU proportional to its length. Ten years is far beyond any
    /// business process this is meant to serve.
    /// </summary>
    private static readonly TimeSpan _maximumDelay = TimeSpan.FromDays(3650);

    private readonly HashSet<DayOfWeek> _nonWorkingDays;
    private readonly TimeZoneInfo _timeZone;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeekdayBusinessCalendar"/> class whose working
    /// days are delimited by UTC midnight.
    /// </summary>
    /// <param name="nonWorkingDays">
    /// The weekdays on which no work happens. Defaults to Saturday and Sunday when <c>null</c>.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when every weekday is marked as non-working, which would make a delay unresolvable.</exception>
    public WeekdayBusinessCalendar(IEnumerable<DayOfWeek>? nonWorkingDays = null)
        : this(nonWorkingDays, timeZone: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WeekdayBusinessCalendar"/> class.
    /// </summary>
    /// <param name="nonWorkingDays">
    /// The weekdays on which no work happens. Defaults to Saturday and Sunday when <c>null</c>.
    /// </param>
    /// <param name="timeZone">
    /// The zone whose midnight separates one working day from the next. Defaults to
    /// <see cref="TimeZoneInfo.Utc"/> when <c>null</c>.
    /// </param>
    /// <remarks>
    /// A working day is a local notion, and the callers inside the framework hand the calendar a UTC
    /// instant — the saga timeout scheduler does. Without a zone of its own, an office in Berlin has
    /// its weekend start and end at two in the morning, and every delay crossing a weekend is off by
    /// the offset. The zone given here is what the day boundaries are read in; the instants going in
    /// and coming out are unaffected.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when every weekday is marked as non-working, which would make a delay unresolvable.</exception>
    public WeekdayBusinessCalendar(IEnumerable<DayOfWeek>? nonWorkingDays, TimeZoneInfo? timeZone)
    {
        _nonWorkingDays = nonWorkingDays is null
            ? [DayOfWeek.Saturday, DayOfWeek.Sunday]
            : [.. nonWorkingDays];

        if (_nonWorkingDays.Count >= DaysPerWeek)
            throw new ArgumentException("At least one weekday must be a working day.", nameof(nonWorkingDays));

        _timeZone = timeZone ?? TimeZoneInfo.Utc;
    }

    /// <inheritdoc />
    public bool IsBusinessDay(DateOnly date) => !_nonWorkingDays.Contains(date.DayOfWeek);

    /// <inheritdoc />
    public DateTimeOffset Add(DateTimeOffset from, TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
            return from;
        if (delay > _maximumDelay)
            throw new ArgumentOutOfRangeException(nameof(delay), $"A business-day delay may not exceed {_maximumDelay.TotalDays} days.");

        // Which day it is, and where that day ends, are read in the calendar's own zone rather than in
        // whatever offset the caller's instant happens to carry.
        var cursor = TimeZoneInfo.ConvertTime(from, _timeZone);
        var remaining = delay;

        // Walk forward and let only business days consume the remaining delay. The loop always
        // terminates because the constructor guarantees at least one working weekday.
        while (true)
        {
            var day = DateOnly.FromDateTime(cursor.Date);

            if (!IsBusinessDay(day))
            {
                // A non-working day consumes nothing, and neither does the run of non-working days
                // behind it, so the whole run is one step. Only the day it lands on has a boundary
                // worth reading, which is what keeps a ten-year delay over a one-day week from
                // resolving the zone offset twenty-five thousand times.
                cursor = StartOfDay(cursor.Date.AddDays(NonWorkingRun(day.DayOfWeek)));

                continue;
            }

            var startOfNextDay = StartOfDay(cursor.Date.AddDays(1));
            var availableToday = startOfNextDay - cursor;

            if (availableToday >= remaining)
                return (cursor + remaining).ToOffset(from.Offset);

            remaining -= availableToday;
            cursor = startOfNextDay;
        }
    }

    /// <summary>
    /// Counts the non-working days from <paramref name="day"/> up to the next working one.
    /// </summary>
    /// <param name="day">A weekday already known to be non-working.</param>
    /// <returns>How many days forward the next working day lies, at least one and at most six.</returns>
    /// <remarks>
    /// Bounded by the constructor's guarantee that some weekday is a working one, so the walk around
    /// the week always finds it.
    /// </remarks>
    private int NonWorkingRun(DayOfWeek day)
    {
        var run = 1;

        while (_nonWorkingDays.Contains((DayOfWeek)(((int)day + run) % DaysPerWeek)))
        {
            run++;
        }

        return run;
    }

    /// <summary>
    /// Reads the instant at which a local date begins in this calendar's zone.
    /// </summary>
    /// <remarks>
    /// The offset is looked up per date rather than carried over from the cursor, so a day that a
    /// daylight-saving transition makes 23 or 25 hours long contributes exactly that much business
    /// time. A midnight that a transition skips altogether — rare, but real in a few zones — resolves
    /// to the standard offset, which puts the boundary an hour off for that single day rather than
    /// throwing.
    /// </remarks>
    private DateTimeOffset StartOfDay(DateTime localDate) =>
        new(localDate, _timeZone.GetUtcOffset(localDate));
}
