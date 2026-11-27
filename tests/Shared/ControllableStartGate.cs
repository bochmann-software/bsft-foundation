using Bsft.Core.Hosting;

namespace Bsft.Tests.Shared;

/// <summary>
/// A start gate a test opens by hand, so it can assert that a hosted loop is waiting before it opens the gate
/// and running after.
/// </summary>
internal sealed class ControllableStartGate : IHostedWorkStartGate, IHostedWorkStartGateOverdueSignal
{
    private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _awaited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets a task that completes when something has started waiting on the gate.</summary>
    public Task Awaited => _awaited.Task;

    /// <summary>Gets or sets what <see cref="IsOverdue"/> answers.</summary>
    public bool Overdue { get; set; }

    /// <summary>Gets how many times the gate has been awaited.</summary>
    public int WaitCount { get; private set; }

    /// <summary>Opens the gate, which completes every wait on it.</summary>
    public void Open() => _open.TrySetResult();

    /// <inheritdoc />
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        WaitCount++;
        _awaited.TrySetResult();

        return _open.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public bool IsOverdue() => Overdue;
}
