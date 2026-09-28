namespace Bsft.Scheduling;

/// <summary>
/// The recurrence rule of a job: either a cron expression or a fixed interval.
/// </summary>
/// <remarks>
/// A schedule only ever answers "when is the next occurrence after this instant". It holds no state
/// and no clock of its own, which is what lets the dispatcher compute occurrences from a
/// <see cref="TimeProvider"/> and lets tests drive them from virtual time.
/// </remarks>
public sealed class JobSchedule
{
    private readonly CronExpression? _cron;
    private readonly TimeSpan _interval;

    private JobSchedule(CronExpression? cron, TimeSpan interval)
    {
        _cron = cron;
        _interval = interval;
    }

    /// <summary>Gets the cron expression text, or <c>null</c> when this is an interval schedule.</summary>
    public string? CronExpressionText => _cron?.Expression;

    /// <summary>Gets the interval, or <c>null</c> when this is a cron schedule.</summary>
    public TimeSpan? Interval => _cron is null ? _interval : null;

    /// <summary>
    /// Creates a schedule from a five-field cron expression.
    /// </summary>
    /// <param name="expression">The cron expression, for example <c>0 3 * * MON-FRI</c>.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="expression"/> is null.</exception>
    /// <exception cref="CronFormatException">Thrown when the expression is not valid.</exception>
    public static JobSchedule Cron(string expression) => new(CronExpression.Parse(expression), TimeSpan.Zero);

    /// <summary>
    /// Creates a schedule that repeats at a fixed interval.
    /// </summary>
    /// <param name="interval">The gap between occurrences. Must be positive.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the interval is not positive.</exception>
    public static JobSchedule Every(TimeSpan interval)
    {
        // A zero or negative interval would make every occurrence due the instant it is created, so
        // the dispatcher would spin on that one job and starve every other.
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "The interval must be positive.");

        return new JobSchedule(null, interval);
    }

    /// <summary>
    /// Computes the first occurrence strictly after the given instant.
    /// </summary>
    /// <param name="after">The instant to search from, exclusive.</param>
    /// <returns>
    /// The next occurrence in UTC, or <c>null</c> when the cron expression can never match again.
    /// </returns>
    public DateTimeOffset? FindNextOccurrence(DateTimeOffset after) =>
        _cron is not null ? _cron.FindNextOccurrence(after) : after + _interval;

    /// <summary>Returns a readable description of the schedule.</summary>
    /// <returns>The cron expression, or the interval.</returns>
    public override string ToString() => _cron is not null ? _cron.Expression : $"every {_interval}";
}
