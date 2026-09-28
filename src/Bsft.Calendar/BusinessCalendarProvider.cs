using Microsoft.Extensions.Options;

namespace Bsft.Calendar;

/// <summary>
/// Default <see cref="IBusinessCalendarProvider"/> backed by <see cref="BsftCalendarOptions"/>.
/// </summary>
internal sealed class BusinessCalendarProvider : IBusinessCalendarProvider
{
    private readonly IOptions<BsftCalendarOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="BusinessCalendarProvider"/> class.
    /// </summary>
    /// <param name="options">The registered calendars.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public BusinessCalendarProvider(IOptions<BsftCalendarOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public IBusinessCalendar Get(string? calendarKey)
    {
        if (calendarKey is null)
            return _options.Value.Default;

        // An unknown key is a configuration error. Silently falling back to the default calendar
        // would compute business-day delays with the wrong rules and nobody would notice.
        if (!_options.Value.Calendars.TryGetValue(calendarKey, out var calendar))
            throw new InvalidOperationException($"No business calendar is registered under the key '{calendarKey}'.");

        return calendar;
    }
}
