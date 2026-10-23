using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// Runs the rules of <see cref="BsftBoundedPipelineOptions"/> under <c>ValidateOnStart()</c>, collecting
/// every failure instead of throwing at the first.
/// </summary>
internal sealed class BsftBoundedPipelineOptionsValidator : IValidateOptions<BsftBoundedPipelineOptions>
{
    /// <summary>The longest wait <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> accepts.</summary>
    private static readonly TimeSpan LongestWait = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly TimeSpan _shutdownTimeout;

    /// <summary>Initializes a new instance of the <see cref="BsftBoundedPipelineOptionsValidator"/> class.</summary>
    /// <param name="host">Supplies the shutdown time the stop budgets have to fit in.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="host"/> is null.</exception>
    public BsftBoundedPipelineOptionsValidator(IOptions<HostOptions> host)
    {
        if (host is null)
            throw new ArgumentNullException(nameof(host));

        _shutdownTimeout = host.Value.ShutdownTimeout;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BsftBoundedPipelineOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var failures = new List<string>();
        RequirePositive(failures, nameof(BsftBoundedPipelineOptions.Capacity), options.Capacity);
        RequirePositive(failures, nameof(BsftBoundedPipelineOptions.MaxBatchSize), options.MaxBatchSize);
        RequirePositive(failures, nameof(BsftBoundedPipelineOptions.EnrichmentCapacity), options.EnrichmentCapacity);
        RequireWait(failures, nameof(BsftBoundedPipelineOptions.FlushInterval), options.FlushInterval);
        RequireWait(failures, nameof(BsftBoundedPipelineOptions.FlushOnStopTimeout), options.FlushOnStopTimeout);
        RequireWait(failures, nameof(BsftBoundedPipelineOptions.EnrichmentTimeout), options.EnrichmentTimeout);
        RequireWait(failures, nameof(BsftBoundedPipelineOptions.EnrichmentOnStopTimeout), options.EnrichmentOnStopTimeout);

        // The stop budgets are spent one after the other inside the host's shutdown time; a pair that
        // does not fit is flushed against an orchestrator's kill, so it is refused at start.
        var stopBudget = options.FlushOnStopTimeout + (options.HasEnrichmentStage ? options.EnrichmentOnStopTimeout : TimeSpan.Zero);
        if (_shutdownTimeout > TimeSpan.Zero && stopBudget > _shutdownTimeout)
        {
            var budgets = options.HasEnrichmentStage
                ? $"{nameof(BsftBoundedPipelineOptions.FlushOnStopTimeout)} + {nameof(BsftBoundedPipelineOptions.EnrichmentOnStopTimeout)}"
                : nameof(BsftBoundedPipelineOptions.FlushOnStopTimeout);
            failures.Add($"The stop budgets ({budgets} = {stopBudget}) must fit in HostOptions.ShutdownTimeout ({_shutdownTimeout}).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void RequirePositive(List<string> failures, string property, int value)
    {
        if (value <= 0)
            failures.Add($"{property} must be positive, but it is {value}.");
    }

    private static void RequireWait(List<string> failures, string property, TimeSpan value)
    {
        if (value <= TimeSpan.Zero || value > LongestWait)
            failures.Add($"{property} must be positive and not exceed {LongestWait}, but it is {value}.");
    }
}
