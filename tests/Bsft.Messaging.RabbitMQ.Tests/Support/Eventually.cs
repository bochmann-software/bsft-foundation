namespace Bsft.Messaging.RabbitMQ.Tests.Support;

/// <summary>
/// Waits for a step of the pipeline to have happened. The host runs on its own task, so a test cannot
/// await it directly; what it can do is wait on the fakes' own wakeups instead of asking repeatedly,
/// and refuse to wait forever.
/// </summary>
internal static class Eventually
{
    /// <summary>How long a step of the pipeline is given before the test calls it a failure.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>Waits until a condition holds, woken by whichever of the given fakes changes first.</summary>
    /// <param name="condition">The state the pipeline is expected to reach.</param>
    /// <param name="signals">The wakeups of every fake the condition reads from.</param>
    /// <returns>A task that completes once the condition holds.</returns>
    /// <exception cref="TimeoutException">Thrown when the condition does not hold in time.</exception>
    public static async Task HoldsAsync(Func<bool> condition, params ChangeSignal[] signals)
    {
        if (signals is null || signals.Length == 0)
            throw new ArgumentException("At least one signal to wait on is required.", nameof(signals));

        using var guard = new CancellationTokenSource(Patience);
        try
        {
            while (true)
            {
                // Captured before the check: a pulse landing between the two already shows up in the
                // state the condition reads, so either the condition is now true, or the capture is
                // still the next wakeup to come.
                var pending = Task.WhenAny(Array.ConvertAll(signals, signal => signal.WaitAsync(guard.Token)));

                if (condition())
                    return;

                await pending.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (guard.IsCancellationRequested)
        {
            throw new TimeoutException($"The pipeline did not reach the expected state within {Patience}.");
        }
    }
}
