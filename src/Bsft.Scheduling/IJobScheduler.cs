namespace Bsft.Scheduling;

/// <summary>
/// Enqueues one-shot jobs to run after a delay or at a given instant, and cancels them again while
/// they are still pending.
/// </summary>
/// <remarks>
/// The scheduler is scoped rather than singleton, so an enqueue made while handling a request picks
/// up that request's correlation id and carries it into the execution.
/// </remarks>
public interface IJobScheduler
{
    /// <summary>
    /// Enqueues a job to run once, after the given delay.
    /// </summary>
    /// <param name="jobName">The registered job name.</param>
    /// <param name="delay">How long to wait before the job becomes due. A non-positive delay makes it due immediately.</param>
    /// <param name="payload">An opaque payload handed to the job, or <c>null</c>.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The identity of the enqueued run, usable with <see cref="CancelAsync"/>.</returns>
    /// <exception cref="Bsft.Scheduling.JobNotRegisteredException">Thrown when no job of that name is registered.</exception>
    Task<long> EnqueueAsync(string jobName, TimeSpan delay, string? payload = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a job to run once, at the given instant.
    /// </summary>
    /// <param name="jobName">The registered job name.</param>
    /// <param name="dueAt">The instant at which the job becomes due. An instant in the past makes it due immediately.</param>
    /// <param name="payload">An opaque payload handed to the job, or <c>null</c>.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The identity of the enqueued run, usable with <see cref="CancelAsync"/>.</returns>
    /// <exception cref="Bsft.Scheduling.JobNotRegisteredException">Thrown when no job of that name is registered.</exception>
    Task<long> EnqueueAtAsync(string jobName, DateTimeOffset dueAt, string? payload = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a registered job type to run once, after the given delay.
    /// </summary>
    /// <typeparam name="TJob">The registered job type.</typeparam>
    /// <param name="delay">How long to wait before the job becomes due.</param>
    /// <param name="payload">An opaque payload handed to the job, or <c>null</c>.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The identity of the enqueued run.</returns>
    /// <exception cref="Bsft.Scheduling.JobNotRegisteredException">Thrown when the type is not registered.</exception>
    Task<long> EnqueueAsync<TJob>(TimeSpan delay, string? payload = null, CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <summary>
    /// Cancels a run that has not started executing yet.
    /// </summary>
    /// <param name="jobId">The identity returned by an enqueue call.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns><c>true</c> when a pending run was cancelled; <c>false</c> when it had already started or was gone.</returns>
    Task<bool> CancelAsync(long jobId, CancellationToken cancellationToken = default);
}
