namespace Bsft.Mediator;

/// <summary>
/// Emits traces and metrics for mediator request and notification handling. A scope is opened per
/// dispatch and closed (disposed) when handling completes, recording duration and outcome.
/// </summary>
public interface IMediatorTelemetry
{
    /// <summary>
    /// Begins a telemetry scope for a command or query dispatch.
    /// </summary>
    /// <param name="requestName">The short request type name (used as span name and metric tag).</param>
    /// <param name="requestType">The full request type name (recorded as a span tag).</param>
    /// <param name="correlationId">The current correlation ID, recorded as a span tag when well-formed.</param>
    /// <returns>A scope that must be disposed when handling finishes.</returns>
    IRequestTelemetryScope BeginRequest(string requestName, string requestType, string? correlationId);

    /// <summary>
    /// Begins a telemetry scope for a notification publish.
    /// </summary>
    /// <param name="notificationName">The short notification type name.</param>
    /// <param name="handlerCount">The number of handlers the notification is dispatched to.</param>
    /// <returns>A scope that must be disposed when publishing finishes.</returns>
    IRequestTelemetryScope BeginPublish(string notificationName, int handlerCount);
}
