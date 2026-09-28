namespace Bsft.Calendar;

/// <summary>
/// Translates a delay into an absolute instant while respecting the working days of an organisation.
/// </summary>
/// <remarks>
/// A delay is treated as an amount of <em>business time</em>: it only accrues on days for which
/// <see cref="IsBusinessDay"/> returns <c>true</c>. A two day delay starting on a Friday therefore
/// lands on the following Tuesday when Saturday and Sunday are not business days.
/// </remarks>
public interface IBusinessCalendar
{
    /// <summary>
    /// Gets a value indicating whether the given date is a working day for this calendar.
    /// </summary>
    /// <param name="date">The date to test.</param>
    /// <returns><c>true</c> when work happens on that date; otherwise <c>false</c>.</returns>
    bool IsBusinessDay(DateOnly date);

    /// <summary>
    /// Adds a delay to an instant, skipping the time that falls on non-business days.
    /// </summary>
    /// <param name="from">The instant the delay starts from.</param>
    /// <param name="delay">The amount of business time to add. Zero or negative delays return <paramref name="from"/> unchanged.</param>
    /// <returns>The instant at which the delay has elapsed.</returns>
    DateTimeOffset Add(DateTimeOffset from, TimeSpan delay);
}
