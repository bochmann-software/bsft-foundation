using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bsft.Outbox;

/// <summary>
/// Refuses a drain worker whose shutdown grace cannot be waited, or does not fit into the time the host
/// gives its services to stop, while the host starts.
/// </summary>
/// <remarks>
/// The host stops its services within <see cref="HostOptions.ShutdownTimeout"/> and then ends the
/// process regardless. A grace at or beyond that timeout promises a row time the host will not give it,
/// and the process ends in the middle of the row, which is the duplicate the grace exists to prevent.
/// It is refused rather than clamped: a clamp would run with a number nobody wrote down, and the
/// timeout is shared by every hosted service, so what is left for this one depends on the others.
/// Raise <c>HostOptions.ShutdownTimeout</c> or lower the grace.
/// </remarks>
internal sealed class BsftOutboxDrainWorkerOptionsValidator : IValidateOptions<BsftOutboxDrainWorkerOptions>
{
    /// <summary>The longest grace a timer can count down: <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> takes whole milliseconds up to <see cref="int.MaxValue"/>.</summary>
    private static readonly TimeSpan LongestGrace = TimeSpan.FromMilliseconds(int.MaxValue);

    private readonly HostOptions _hostOptions;

    /// <summary>Initializes a new instance of the <see cref="BsftOutboxDrainWorkerOptionsValidator"/> class.</summary>
    /// <param name="hostOptions">The host options, read for the shutdown timeout.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="hostOptions"/> is null.</exception>
    public BsftOutboxDrainWorkerOptionsValidator(IOptions<HostOptions> hostOptions) =>
        _hostOptions = (hostOptions ?? throw new ArgumentNullException(nameof(hostOptions))).Value;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BsftOutboxDrainWorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var subject = name is null ? "The outbox drain worker" : $"The drain worker of the outbox named '{name}'";
        var grace = options.ShutdownGrace;

        if (grace < TimeSpan.Zero || grace > LongestGrace)
        {
            return ValidateOptionsResult.Fail(
                $"{subject} is misconfigured. {nameof(BsftOutboxDrainWorkerOptions.ShutdownGrace)} must not be negative and at most {LongestGrace}, but was {grace}.");
        }

        var timeout = _hostOptions.ShutdownTimeout;

        // An infinite or zero timeout means the host does not bound the stop (infinite) or does not
        // wait at all (zero); only a finite positive one can be outrun.
        if (timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan && grace >= timeout)
        {
            return ValidateOptionsResult.Fail(
                $"{subject} is misconfigured. {nameof(BsftOutboxDrainWorkerOptions.ShutdownGrace)} ({grace}) must be shorter than {nameof(HostOptions)}.{nameof(HostOptions.ShutdownTimeout)} ({timeout}), or the host ends the process in the middle of the row the grace was meant to protect.");
        }

        return ValidateOptionsResult.Success;
    }
}
