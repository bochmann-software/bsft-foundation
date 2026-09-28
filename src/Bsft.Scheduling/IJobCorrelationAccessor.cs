namespace Bsft.Scheduling;

/// <summary>
/// Supplies the correlation id that is stored with a scheduled run and restored when it executes, so
/// that logs, traces and the audit trail can follow a request through a job that runs hours later.
/// </summary>
/// <remarks>
/// <c>Bsft.Scheduling</c> does not reference <c>Bsft.Correlation</c>: that would drag the ASP.NET Core
/// request pipeline into a background component. Register the adapter from
/// <c>Bsft.Scheduling.Correlation</c> to connect the two. Without it, runs carry no correlation id
/// and the scheduler behaves exactly as before.
/// </remarks>
public interface IJobCorrelationAccessor
{
    /// <summary>Gets the correlation id of the current execution, or <c>null</c> when unknown.</summary>
    string? CorrelationId { get; }
}
