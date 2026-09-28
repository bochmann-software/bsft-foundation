namespace Bsft.Mediator;

/// <summary>
/// Provides access to the correlation ID for the current scope.
/// Implement this interface to integrate correlation tracking from external packages
/// (e.g. Bsft.Correlation) into the mediator pipeline.
/// </summary>
/// <remarks>
/// What this returns reaches a log line and a trace tag, so an implementation that surfaces an
/// inbound header should return a bounded, well-formed value and fall back to one it generated
/// itself. The mediator does not take that on trust: it records the value only when it is well
/// formed and substitutes a placeholder otherwise, so a caller cannot write its own log lines
/// through a crafted header.
/// </remarks>
public interface ICorrelationIdAccessor
{
    /// <summary>
    /// Gets the correlation ID for the current request.
    /// </summary>
    /// <value>The correlation ID, which is recorded only when it is well formed.</value>
    string CorrelationId { get; }
}
