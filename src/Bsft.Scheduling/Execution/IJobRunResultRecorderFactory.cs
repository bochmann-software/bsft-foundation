namespace Bsft.Scheduling.Execution;

/// <summary>
/// Creates the <see cref="IJobRunResultRecorder"/> a job is handed for one execution.
/// </summary>
/// <remarks>
/// A seam of its own so that how a result is serialized and bounded is decided in one place the
/// container wires, rather than inside the executor that runs the job.
/// </remarks>
internal interface IJobRunResultRecorderFactory
{
    /// <summary>Creates the recorder that puts a job's result on the claimed run.</summary>
    /// <param name="run">The claimed run the result is recorded on.</param>
    /// <returns>The recorder for this execution.</returns>
    IJobRunResultRecorder Create(JobRun run);
}
