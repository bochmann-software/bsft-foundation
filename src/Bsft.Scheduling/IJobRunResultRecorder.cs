namespace Bsft.Scheduling;

/// <summary>
/// Where a job records what it reports about the run it is executing, reached through
/// <see cref="JobExecutionContext.ResultRecorder"/>.
/// </summary>
/// <remarks>
/// <para>
/// Recording is optional. A job that records nothing settles exactly as before, with no result on the
/// run. A job that records several times keeps the last result, so a job may record its counters as
/// it goes and the run still carries the latest state if a later step fails.
/// </para>
/// <para>
/// The result is stored when the attempt settles the run, whether it completed or failed for good. An
/// attempt that fails and is retried leaves no result behind; the attempt that settles the run records
/// its own. A value recorded after <see cref="IJob.ExecuteAsync"/> has returned is not guaranteed to be
/// stored.
/// </para>
/// </remarks>
public interface IJobRunResultRecorder
{
    /// <summary>
    /// Records the result of the run, replacing any result recorded before.
    /// </summary>
    /// <typeparam name="TResult">The type the result is serialized from.</typeparam>
    /// <param name="schema">
    /// The name that says how the value is to be read, such as <c>mail-dispatch/v1</c>, at most
    /// <see cref="JobRunResult.MaxSchemaLength"/> characters long.
    /// </param>
    /// <param name="result">The value, serialized as JSON with the web defaults (camelCase names).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="schema"/> is blank or too long.</exception>
    /// <exception cref="Bsft.Core.PayloadTooLargeException">
    /// Thrown with <see cref="SchedulingErrorCodes.ResultTooLarge"/> when the serialized value exceeds
    /// <see cref="BsftSchedulingOptions.MaxResultBytes"/>. Nothing is recorded and an earlier result is
    /// kept. The run itself is not affected unless the job lets the exception escape, in which case the
    /// attempt fails like any other.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the context was not created by the scheduler, so there is no run to record on.
    /// </exception>
    void Record<TResult>(string schema, TResult result);
}
