namespace Bsft.EventAggregator.Saga.Timeouts;

/// <summary>
/// Publishes the event carried by a due saga timeout through the event aggregator.
/// </summary>
/// <remarks>
/// Separated from <see cref="Bsft.EventAggregator.Saga.Timeouts.SagaTimeoutDispatcher"/> so that the polling, catch-up and telemetry
/// concerns stay clear of payload deserialization, correlation restoration and the reflective
/// dispatch onto the closed generic publish method.
/// </remarks>
public interface ISagaTimeoutPublisher
{
    /// <summary>
    /// Deserializes the timeout's payload and publishes it under the timeout's correlation id.
    /// </summary>
    /// <param name="timeout">The timeout whose carried event is published.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes once every subscriber has run.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeout"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the payload type cannot be resolved or the payload deserializes to null.
    /// </exception>
    Task PublishAsync(SagaTimeout timeout, CancellationToken cancellationToken = default);
}
