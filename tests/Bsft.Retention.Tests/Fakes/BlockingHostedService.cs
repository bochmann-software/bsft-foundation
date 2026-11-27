using Microsoft.Extensions.Hosting;

namespace Bsft.Retention.Tests.Fakes;

/// <summary>
/// A hosted service whose start blocks until the test releases it, standing in for one that creates or
/// migrates the database a retention pass deletes from.
/// </summary>
internal sealed class BlockingHostedService : IHostedService
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets a task that completes once the host has called <see cref="StartAsync"/>.</summary>
    public Task Entered => _entered.Task;

    /// <summary>Lets <see cref="StartAsync"/> complete.</summary>
    public void Release() => _release.TrySetResult();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _entered.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
