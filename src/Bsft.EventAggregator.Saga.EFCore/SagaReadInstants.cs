namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// Reads the instants the saga tables store.
/// </summary>
/// <remarks>
/// Shared by the two readers rather than written twice, and the assertion in it is the point: a
/// provider that hands back an unspecified kind - SQLite does - would otherwise be read as local time,
/// which shifts every due instant by the host's offset.
/// </remarks>
internal static class SagaReadInstants
{
    /// <summary>Reads a stored UTC timestamp as the instant it is.</summary>
    /// <param name="value">The stored value.</param>
    /// <returns>The instant.</returns>
    internal static DateTimeOffset Moment(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>Reads an optional stored timestamp.</summary>
    /// <param name="value">The stored value, or null.</param>
    /// <returns>The instant, or null.</returns>
    internal static DateTimeOffset? Moment(DateTime? value) => value is { } moment ? Moment(moment) : null;
}
