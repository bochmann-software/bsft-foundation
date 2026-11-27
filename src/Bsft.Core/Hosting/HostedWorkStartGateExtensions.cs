namespace Bsft.Core.Hosting;

/// <summary>
/// Awaits the start gates a hosted loop was handed.
/// </summary>
public static class HostedWorkStartGateExtensions
{
    /// <summary>Waits for every gate, one after the other in registration order.</summary>
    /// <param name="gates">The gates the loop was handed, or null when the host registered none.</param>
    /// <param name="cancellationToken">A cancellation token that is cancelled when the host stops.</param>
    /// <returns>A task that completes when every gate is open. It is already completed when there are none.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the token is cancelled while a gate is pending.</exception>
    public static Task WaitAllAsync(this IEnumerable<IHostedWorkStartGate>? gates, CancellationToken cancellationToken = default) =>
        gates is null ? Task.CompletedTask : WaitEachAsync(gates, cancellationToken);

    /// <summary>
    /// Waits for every gate and answers whether they all opened, as opposed to the host stopping first. It
    /// waits: the name asks the question the caller needs answered once the wait is over.
    /// </summary>
    /// <param name="gates">The gates the loop was handed, or null when the host registered none.</param>
    /// <param name="stoppingToken">The token that is cancelled when the host stops.</param>
    /// <returns><c>true</c> when every gate is open, <c>false</c> when the stopping token was cancelled while a gate was pending.</returns>
    /// <remarks>A gate that throws anything else, or cancels on a token of its own, still faults the caller.</remarks>
    public static async Task<bool> HasOpenedAsync(this IEnumerable<IHostedWorkStartGate>? gates, CancellationToken stoppingToken = default)
    {
        try
        {
            await gates.WaitAllAsync(stoppingToken).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task WaitEachAsync(IEnumerable<IHostedWorkStartGate> gates, CancellationToken cancellationToken)
    {
        foreach (var gate in gates)
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
