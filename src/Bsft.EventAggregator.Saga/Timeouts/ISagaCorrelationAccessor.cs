namespace Bsft.EventAggregator.Saga.Timeouts;

/// <summary>
/// Supplies the correlation id that is stored with a timeout and restored when it fires, so that
/// logs, traces and the audit trail can follow a request through a timer that fires days later.
/// </summary>
/// <remarks>
/// The saga packages do not reference <c>Bsft.Correlation</c> — that would drag the ASP.NET Core
/// request pipeline into a background component. Register the adapter from
/// <c>Bsft.EventAggregator.Saga.Correlation</c> to connect the two.
/// </remarks>
public interface ISagaCorrelationAccessor
{
    /// <summary>Gets the correlation id of the current execution, or <c>null</c> when unknown.</summary>
    string? CorrelationId { get; }
}
