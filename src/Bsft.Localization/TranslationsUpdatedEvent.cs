namespace Bsft.Localization;

/// <summary>
/// Published via <see cref="Bsft.EventAggregator.IEventAggregator"/> when translations are modified.
/// Subscribers should invalidate their translation caches in response.
/// </summary>
/// <remarks>
/// A record, like every other event this framework publishes through the aggregator: an event is
/// what happened, and two of them carrying the same narrowing are the same event.
/// </remarks>
public sealed record TranslationsUpdatedEvent
{
    /// <summary>
    /// Gets the locale that was updated, or <c>null</c> when the update applies to all locales.
    /// </summary>
    public string? Locale { get; init; }

    /// <summary>
    /// Gets the tenant identifier whose translations changed, or <c>null</c> for global translations.
    /// </summary>
    public string? TenantId { get; init; }
}
