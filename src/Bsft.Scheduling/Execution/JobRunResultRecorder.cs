using System.Text.Json;
using Bsft.Core;

namespace Bsft.Scheduling.Execution;

/// <summary>
/// The <see cref="IJobRunResultRecorder"/> the executor hands a job: it bounds and serializes the value
/// and puts it on the claimed run, which the outcome policy then settles.
/// </summary>
/// <remarks>
/// The value is serialized at the moment it is recorded rather than when the run settles, so a value
/// that cannot be serialized or is too large fails in the job's own call, where the job can still
/// decide what to do about it, instead of in the settlement after the work is done.
/// </remarks>
internal sealed class JobRunResultRecorder : IJobRunResultRecorder
{
    /// <summary>
    /// The serializer settings every result is written with. Held once, because the serializer caches
    /// its type metadata per options instance.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly JobRun _run;
    private readonly int _maxResultBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobRunResultRecorder"/> class.
    /// </summary>
    /// <param name="run">The claimed run the result is recorded on.</param>
    /// <param name="maxResultBytes">The largest serialized value accepted, in UTF-8 bytes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="run"/> is null.</exception>
    public JobRunResultRecorder(JobRun run, int maxResultBytes)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _maxResultBytes = maxResultBytes;
    }

    /// <inheritdoc />
    public void Record<TResult>(string schema, TResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, SerializerOptions);

        if (bytes.Length > _maxResultBytes)
        {
            throw new PayloadTooLargeException(
                $"The result recorded for job {_run.JobName} run {_run.Id} is {bytes.Length} bytes, more than the {_maxResultBytes} bytes {nameof(BsftSchedulingOptions)}.{nameof(BsftSchedulingOptions.MaxResultBytes)} allows. It was not recorded.",
                SchedulingErrorCodes.ResultTooLarge);
        }

        _run.Result = new JobRunResult(schema, JsonSerializer.Deserialize<JsonElement>(bytes));
    }
}
