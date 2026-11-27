namespace Bsft.Logging;

/// <summary>
/// Takes a <see cref="SemaphoreSlim"/> as a hold that is released by disposing it.
/// </summary>
/// <remarks>
/// The writer's gate is not reentrant, so a holder that needs the writer again passes the held gate on instead of
/// entering twice. Entering hands back the release, which keeps the pairing with the caller's <c>using</c>.
/// </remarks>
internal static class SemaphoreSlimExtensions
{
    /// <summary>Waits for the semaphore for as long as it takes.</summary>
    /// <param name="gate">The semaphore to take.</param>
    /// <returns>The hold; disposing it releases the semaphore.</returns>
    public static IDisposable Hold(this SemaphoreSlim gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        gate.Wait();

        return new Release(gate);
    }

    /// <summary>Waits for the semaphore until a budget runs out.</summary>
    /// <param name="gate">The semaphore to take.</param>
    /// <param name="cancellationToken">Cancelled when the budget has run out.</param>
    /// <returns>The hold, or <see langword="null"/> when the budget ran out first.</returns>
    public static IDisposable? HoldWithin(this SemaphoreSlim gate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gate);

        try
        {
            gate.Wait(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() => gate.Release();
    }
}
