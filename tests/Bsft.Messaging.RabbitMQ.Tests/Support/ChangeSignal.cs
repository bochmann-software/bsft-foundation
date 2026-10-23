namespace Bsft.Messaging.RabbitMQ.Tests.Support;

/// <summary>
/// A wakeup a test can await instead of polling. A fake pulses it whenever it records something a test
/// might be waiting on; a waiter captures the current wakeup, checks its own condition, and only then
/// awaits what it captured, so a pulse that lands between the two can never be missed.
/// </summary>
internal sealed class ChangeSignal
{
    private TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Wakes up everyone currently waiting, as a fake does right after it changes its state.</summary>
    public void Pulse()
    {
        var completed = Interlocked.Exchange(ref _tcs, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        completed.TrySetResult();
    }

    /// <summary>
    /// Captures the next wakeup. Call this before checking the condition it relates to, not after: a
    /// pulse that happens in between is then already reflected in the state the condition reads, so
    /// nothing is lost either way.
    /// </summary>
    public Task WaitAsync(CancellationToken cancellationToken) =>
        Volatile.Read(ref _tcs).Task.WaitAsync(cancellationToken);
}
