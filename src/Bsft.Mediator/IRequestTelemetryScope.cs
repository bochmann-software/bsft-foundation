namespace Bsft.Mediator;

/// <summary>
/// A per-dispatch telemetry scope. Call exactly one of <see cref="Complete"/> or
/// <see cref="Fail"/> before disposal; disposal records the duration and outcome.
/// </summary>
public interface IRequestTelemetryScope : IDisposable
{
    /// <summary>Marks the dispatch as successful.</summary>
    void Complete();

    /// <summary>
    /// Marks the dispatch as failed. Expected framework exceptions
    /// (<see cref="Bsft.Core.BsftException"/>) are recorded as an expected outcome and do
    /// not mark the span as an error; all other exceptions set the span status to error.
    /// </summary>
    /// <param name="exception">The exception that caused the failure.</param>
    void Fail(Exception exception);
}
