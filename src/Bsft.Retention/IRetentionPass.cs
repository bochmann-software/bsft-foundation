namespace Bsft.Retention;

/// <summary>
/// One bounded batch of an application's retention rules: delete at most <c>batchSize</c> records
/// that are older than <c>olderThan</c>, and say how many went.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole contract a purge implementation has to satisfy. The loop, the retention window,
/// the pacing between batches, the startup stagger, the failure handling and the back-off are the
/// framework's — supplied by <c>AddBsftRetention</c> — and only the delete statement is the
/// application's. That split is the point: every purge job written against a scheduler before this
/// existed re-implemented the same loop, and each copy got a slightly different part of it wrong.
/// </para>
/// <para>
/// An implementation is resolved from a fresh dependency injection scope for every sweep, so it may
/// take scoped dependencies such as a <c>DbContext</c> through its constructor.
/// </para>
/// </remarks>
public interface IRetentionPass
{
    /// <summary>
    /// Deletes at most <paramref name="batchSize"/> records older than <paramref name="olderThan"/>.
    /// </summary>
    /// <param name="batchSize">
    /// The most records this call may delete. An implementation must honour it: the cap is what keeps
    /// a first sweep against a large backlog from becoming one long-locking transaction, and it is
    /// also how the caller tells a drained backlog from a full batch.
    /// </param>
    /// <param name="olderThan">
    /// The cutoff, computed by the caller from the configured retention and a <see cref="TimeProvider"/>.
    /// A record at or after this instant must be left alone.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe. It is the host stopping.</param>
    /// <returns>
    /// How many records were deleted. A call that returns <paramref name="batchSize"/> is taken to
    /// mean more is waiting and the next batch follows immediately, subject to the configured pacing
    /// delay; anything less ends the sweep until the next interval.
    /// </returns>
    /// <remarks>
    /// A pass may fail in any way it likes: the failure is logged and the next sweep runs on schedule.
    /// Deletion is retried rather than transactional across batches, so an implementation must be
    /// safe to run again after a partial sweep.
    /// </remarks>
    Task<int> PurgeBatchAsync(int batchSize, DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}
