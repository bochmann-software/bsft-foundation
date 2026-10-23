namespace Bsft.Core.Concurrency;

/// <summary>
/// One gate per key: callers asking for the same key run one at a time, callers asking for
/// different keys do not wait for each other.
/// </summary>
/// <typeparam name="TKey">The key that names what is guarded, a directory or a tenant for example.</typeparam>
public interface IKeyedGate<TKey>
    where TKey : notnull
{
    /// <summary>Waits until no other caller holds <paramref name="key"/>.</summary>
    /// <param name="key">The key to guard.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The held gate; disposing it lets the next waiter for the key in.</returns>
    Task<IDisposable> EnterAsync(TKey key, CancellationToken cancellationToken = default);
}
