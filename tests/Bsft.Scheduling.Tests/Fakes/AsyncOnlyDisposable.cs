namespace Bsft.Scheduling.Tests.Fakes;

/// <summary>
/// A scoped service that is only <see cref="IAsyncDisposable"/>, like a leased data source: the container refuses to
/// dispose a scope holding it synchronously, so a pass that does fails.
/// </summary>
internal sealed class AsyncOnlyDisposable : IAsyncDisposable
{
    /// <summary>Gets a value indicating whether the service was disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Yields, as a leased data source does, so the caller's await really suspends.
        await Task.Yield();
        IsDisposed = true;
    }
}
