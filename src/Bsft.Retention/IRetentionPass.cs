namespace Bsft.Retention;

/// <summary>
/// One bounded batch of an application's retention rules: delete at most <c>batchSize</c> records
/// that have expired against the cutoff <c>olderThan</c>, and say how many went.
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
    /// Deletes at most <paramref name="batchSize"/> records that have expired against <paramref name="olderThan"/>.
    /// </summary>
    /// <param name="batchSize">
    /// The most records this call may delete. An implementation must honour it: the cap is what keeps
    /// a first sweep against a large backlog from becoming one long-locking transaction, and it is
    /// also how the caller tells a drained backlog from a full batch.
    /// </param>
    /// <param name="olderThan">
    /// The cutoff, computed by the caller from a <see cref="TimeProvider"/>. What it means depends on the
    /// mode, and the pass owns the comparison:
    /// <list type="bullet">
    /// <item><description>
    /// Age mode (the default): the current time less the configured retention, computed once per sweep.
    /// The pass purges records with <c>CreatedAt &lt; olderThan</c>; a record at or after it is left alone.
    /// </description></item>
    /// <item><description>
    /// Absolute-expiry mode (<see cref="BsftRetentionOptions.UseAbsoluteExpiry"/>): the current time,
    /// read again for every batch. The pass purges records with <c>ExpiresAt &lt;= olderThan</c>.
    /// </description></item>
    /// </list>
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token to observe. A stop lets the batch run to completion; the token is cancelled
    /// only once the host's shutdown timeout has elapsed. Whatever the batch ends in after that, an
    /// <see cref="OperationCanceledException"/> or a store's own exception for the cancelled command, the
    /// sweeper reads as the shutdown cancellation and does not log as an error.
    /// </param>
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
