namespace Bsft.Calendar;

/// <summary>
/// Configures the calendars available to <see cref="IBusinessCalendarProvider"/>.
/// </summary>
/// <remarks>
/// This is the package's primary options type, but it deliberately carries no <c>SectionName</c> and
/// the entry point has no <c>IConfiguration</c> overload. Every member of it is an
/// <see cref="IBusinessCalendar"/> — an implementation, chosen in code — and a configuration
/// provider has nothing to bind to one. A host that wants its calendars driven by configuration
/// reads its own section and calls <see cref="AddCalendar"/> with the instances it built.
/// </remarks>
public sealed class BsftCalendarOptions
{
    private readonly Dictionary<string, IBusinessCalendar> _calendars = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the calendar used when no key is supplied. Defaults to a calendar on which every
    /// day is a working day.
    /// </summary>
    public IBusinessCalendar Default { get; set; } = ContinuousBusinessCalendar.Instance;

    /// <summary>
    /// Gets the keyed calendars.
    /// </summary>
    public IReadOnlyDictionary<string, IBusinessCalendar> Calendars => _calendars;

    /// <summary>
    /// Registers a calendar under a key. Saga timeouts and other callers reference that key so the
    /// working-day rules can differ per organisational unit.
    /// </summary>
    /// <param name="key">The key to register under.</param>
    /// <param name="calendar">The calendar instance.</param>
    /// <returns>The same options instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="calendar"/> is null.</exception>
    public BsftCalendarOptions AddCalendar(string key, IBusinessCalendar calendar)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A calendar key is required.", nameof(key));
        if (calendar is null)
            throw new ArgumentNullException(nameof(calendar));

        _calendars[key] = calendar;
        return this;
    }

    /// <summary>Throws when a configured value cannot be used, naming the member.</summary>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used.</exception>
    internal void Validate()
    {
        if (Default is null)
        {
            throw new InvalidOperationException(
                $"{nameof(BsftCalendarOptions)}.{nameof(Default)} must name a calendar, but it is null.");
        }
    }
}
