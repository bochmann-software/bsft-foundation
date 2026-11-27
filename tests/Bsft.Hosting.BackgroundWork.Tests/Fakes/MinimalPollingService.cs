using Microsoft.Extensions.Logging.Abstractions;

namespace Bsft.Hosting.BackgroundWork.Tests.Fakes;

/// <summary>A polling service that overrides only what is abstract, so the defaults of the base are what runs.</summary>
internal sealed class MinimalPollingService(TimeProvider timeProvider) : PollingBackgroundServiceBase(NullLogger.Instance, timeProvider)
{
    private readonly TaskCompletionSource _firstTick = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets a task that completes when the first pass has started.</summary>
    public Task FirstTick => _firstTick.Task;

    /// <inheritdoc />
    protected override TimeSpan PollInterval => TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override async Task<bool> TryTickAsync(CancellationToken cancellationToken)
    {
        _firstTick.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);

        return false;
    }
}
