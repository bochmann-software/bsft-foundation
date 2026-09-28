namespace Bsft.Scheduling;

/// <summary>
/// Controls whether a recurring job may have two executions in flight at the same time.
/// </summary>
public enum JobOverlapPolicy
{
    /// <summary>
    /// The next occurrence is enqueued only once the current run has settled, so two executions of
    /// the same job never overlap. A run that takes longer than its interval therefore delays the
    /// following occurrence instead of stacking a second one behind it. This is the default.
    /// </summary>
    Forbid = 0,

    /// <summary>
    /// The next occurrence is enqueued before the current run starts, so the job keeps ticking on
    /// schedule even while a slow run is still going. Only choose this for work that is safe to run
    /// concurrently with itself.
    /// </summary>
    Allow = 1,
}
