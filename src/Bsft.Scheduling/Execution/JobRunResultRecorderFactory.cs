using Microsoft.Extensions.Options;

namespace Bsft.Scheduling.Execution;

/// <summary>
/// Default <see cref="IJobRunResultRecorderFactory"/>: a <see cref="JobRunResultRecorder"/> bounded by
/// <see cref="BsftSchedulingOptions.MaxResultBytes"/>.
/// </summary>
internal sealed class JobRunResultRecorderFactory : IJobRunResultRecorderFactory
{
    private readonly int _maxResultBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobRunResultRecorderFactory"/> class.
    /// </summary>
    /// <param name="options">Scheduling options, read for the bound on a recorded result.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public JobRunResultRecorderFactory(IOptions<BsftSchedulingOptions> options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _maxResultBytes = options.Value.MaxResultBytes;
    }

    /// <inheritdoc />
    public IJobRunResultRecorder Create(JobRun run) => new JobRunResultRecorder(run, _maxResultBytes);
}
