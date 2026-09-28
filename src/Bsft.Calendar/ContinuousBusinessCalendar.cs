namespace Bsft.Calendar;

/// <summary>
/// A calendar for which every day is a working day. This is the default: a delay is simply added to
/// the starting instant, which is the correct behaviour for anything that is not tied to office hours.
/// </summary>
public sealed class ContinuousBusinessCalendar : IBusinessCalendar
{
    /// <summary>
    /// Gets the shared instance. The calendar is stateless, so a single instance serves all callers.
    /// </summary>
    public static ContinuousBusinessCalendar Instance { get; } = new();

    /// <inheritdoc />
    public bool IsBusinessDay(DateOnly date) => true;

    /// <inheritdoc />
    public DateTimeOffset Add(DateTimeOffset from, TimeSpan delay) =>
        delay <= TimeSpan.Zero ? from : from + delay;
}
