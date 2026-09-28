namespace Bsft.EventAggregator.Saga.Timeouts;

/// <summary>
/// Carries the correlation id of a firing timeout through the asynchronous flow that publishes its
/// event, so that components running in the step's own service scope can pick it up again.
/// </summary>
/// <remarks>
/// The ambient value flows with the asynchronous control flow, so a single registration has to be
/// shared by the writer and every reader. The saga registration therefore adds it as a singleton.
/// </remarks>
public interface ISagaCorrelationScope
{
    /// <summary>Gets the correlation id of the timeout currently being dispatched, if any.</summary>
    string? Current { get; }

    /// <summary>
    /// Sets the ambient correlation id for the current asynchronous flow.
    /// </summary>
    /// <remarks>
    /// Writing the ambient id is a framework concern: only the timeout dispatcher claims a flow for
    /// the correlation of the request that scheduled the timer. Application code reads it through
    /// <see cref="Current"/> or, preferably, through the saga context.
    /// </remarks>
    /// <param name="correlationId">The correlation id to publish under, or <c>null</c> for none.</param>
    /// <returns>A handle that restores the previous value when disposed.</returns>
    IDisposable Use(string? correlationId);
}
