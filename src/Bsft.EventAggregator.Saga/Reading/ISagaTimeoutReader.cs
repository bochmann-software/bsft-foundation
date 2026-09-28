namespace Bsft.EventAggregator.Saga.Reading;

/// <summary>
/// Reads the durable timeouts hanging off one saga instance.
/// </summary>
/// <remarks>
/// A seam of its own rather than a method on <see cref="ISagaInstanceReader"/>, because durable
/// timeouts are opt-in: a host running the in-memory timeout store has instances to list and no
/// timeout table to read. Where this is not registered the instance reads without them, which is the
/// truth about that host rather than an empty list pretending to be one.
/// </remarks>
public interface ISagaTimeoutReader
{
    /// <summary>Reads the timeouts of one instance, soonest due first.</summary>
    /// <param name="sagaType">The saga definition.</param>
    /// <param name="correlationKey">What identifies the instance within its type.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The timeouts, pending and past, in due order.</returns>
    Task<IReadOnlyList<SagaTimeoutView>> ListForAsync(
        string sagaType, string correlationKey, CancellationToken cancellationToken = default);
}
