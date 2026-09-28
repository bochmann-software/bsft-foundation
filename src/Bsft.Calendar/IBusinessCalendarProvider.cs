namespace Bsft.Calendar;

/// <summary>
/// Resolves a business calendar by key so that different organisational units can use different
/// working-day rules within the same application.
/// </summary>
public interface IBusinessCalendarProvider
{
    /// <summary>
    /// Resolves the calendar registered under the given key.
    /// </summary>
    /// <param name="calendarKey">The calendar key, or <c>null</c> to get the default calendar.</param>
    /// <returns>The matching calendar.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no calendar is registered under <paramref name="calendarKey"/>.</exception>
    IBusinessCalendar Get(string? calendarKey);
}
