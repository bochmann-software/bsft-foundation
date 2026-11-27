using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Bsft.Localization;

/// <summary>
/// The default <see cref="ITranslationMissCache"/>: remembers keys that no level of the resolution
/// chain has, for a short time and in bounded number.
/// </summary>
/// <remarks>
/// <para>
/// A cache of its own rather than a <see cref="Microsoft.Extensions.Caching.Memory.MemoryCache"/>
/// with a size limit: such a cache refuses an insert when it is full and compacts later, so expired
/// entries keep their place until something else frees it. Every entry here lives exactly
/// <see cref="BsftLocalizationOptions.MissCacheLifetime"/>, so the order they expire in is the order
/// they were added in, and dropping what has expired is popping the front of a queue. That makes the
/// drop cheap enough to run on every insert, which is what frees the place of an expired entry the
/// moment it is needed.
/// </para>
/// <para>
/// Reads take no lock: <see cref="Contains"/> is one lookup in a concurrent dictionary of the
/// timestamps the entries were added at, with the key's hash already taken. Writers (<see cref="TryAdd"/>,
/// the drop of expired entries and <see cref="Clear"/>) share one lock, and read the clock inside it,
/// so the queue's order is the order of the timestamps and expiry is a pop from the front. The clock
/// is the monotonic <see cref="TimeProvider.GetTimestamp"/>, never the wall clock, which can step
/// back and would put a later entry ahead of an earlier one.
/// </para>
/// <para>
/// A cache full of live misses stores no further one. The share of that cap one tenant may take is
/// deliberately not built yet: one tenant can fill the cache and leave the misses of the others
/// uncached (still bounded per render by the caller). The per-tenant share is deferred.
/// </para>
/// </remarks>
internal sealed class TranslationMissCache : ITranslationMissCache
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<TranslationCacheKey, long> _added = new();
    private readonly Queue<(TranslationCacheKey Key, long AddedAt)> _order = new();
    private readonly TimeProvider _timeProvider;
    private readonly int _maxEntries;
    private readonly TimeSpan _lifetime;

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationMissCache"/> class.
    /// </summary>
    /// <param name="options">The localization options carrying the bound and the lifetime.</param>
    /// <param name="timeProvider">The clock that decides what has expired.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public TranslationMissCache(IOptions<BsftLocalizationOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);

        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        var value = options.Value ?? throw new ArgumentNullException(nameof(options));

        _maxEntries = value.MissCacheMaxEntries;
        _lifetime = value.MissCacheLifetime;
    }

    /// <summary>
    /// Gets how many entries are held, expired ones that have not been dropped yet included.
    /// </summary>
    internal int Count => _added.Count;

    /// <inheritdoc />
    public bool Contains(TranslationCacheKey key)
        => _added.TryGetValue(key, out var addedAt) && !HasExpired(addedAt);

    /// <inheritdoc />
    public bool TryAdd(TranslationCacheKey key)
    {
        lock (_gate)
        {
            // The clock is read under the lock: two writers that read it before taking the lock could
            // enqueue in the opposite order, and the queue would then stop being ordered by expiry.
            var now = _timeProvider.GetTimestamp();

            // Expired entries go first, so that a full cache whose entries have lapsed accepts this one
            // and a key whose own entry lapsed is recorded afresh rather than found.
            DropExpired(now);

            if (_added.ContainsKey(key))
                return true;

            if (_added.Count >= _maxEntries)
                return false;

            _added[key] = now;
            _order.Enqueue((key, now));

            return true;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate)
        {
            _added.Clear();
            _order.Clear();
        }
    }

    private bool HasExpired(long addedAt, long now)
        => _timeProvider.GetElapsedTime(addedAt, now) >= _lifetime;

    private bool HasExpired(long addedAt)
        => HasExpired(addedAt, _timeProvider.GetTimestamp());

    private void DropExpired(long now)
    {
        while (_order.TryPeek(out var oldest) && HasExpired(oldest.AddedAt, now))
        {
            _order.Dequeue();
            _added.TryRemove(oldest.Key, out _);
        }
    }
}
