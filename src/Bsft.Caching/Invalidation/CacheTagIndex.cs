namespace Bsft.Caching.Invalidation;

/// <summary>
/// In-process <see cref="ICacheTagIndex"/> mapping each tag to the keys carrying it, together
/// with the expiry of the entry behind each key and the stamp of the association. Reads drop
/// associations whose entry has expired, so the index does not accumulate keys the store no longer
/// holds.
/// </summary>
/// <remarks>
/// A single lock guards every operation. Associating and invalidating are compound read-modify-
/// write sequences over two levels of dictionaries, and a lock-free structure would let a
/// concurrent association be dropped together with the tag it was being added to, leaving an entry
/// that no invalidation can ever reach. Invalidation is rare compared to cache reads, which do not
/// touch the index at all, so the contention is negligible.
/// </remarks>
public sealed class CacheTagIndex : ICacheTagIndex
{
    /// <summary>How much time passes between two full sweeps over all tags.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, Dictionary<string, (DateTimeOffset ExpiresAt, long Stamp)>> _associationsByTag =
        new(StringComparer.Ordinal);

    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();

    private DateTimeOffset _nextSweep = DateTimeOffset.MinValue;
    private long _lastStamp;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheTagIndex"/> class.
    /// </summary>
    /// <param name="timeProvider">The time source deciding which associations have expired.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    public CacheTagIndex(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public void Associate(string key, IReadOnlyCollection<string> tags, DateTimeOffset expiresAt)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        if (tags is null)
            throw new ArgumentNullException(nameof(tags));

        lock (_lock)
        {
            // Pruning only the tags being touched would leave a tag that is never read or
            // re-associated holding its expired keys forever, so the whole index is swept from
            // time to time.
            SweepIfDue();

            // One stamp per call, shared by every tag this key is being associated with, so a
            // conditional forget either keeps all of them or none.
            var stamp = ++_lastStamp;

            foreach (var tag in tags)
            {
                if (!_associationsByTag.TryGetValue(tag, out var associations))
                {
                    associations = new Dictionary<string, (DateTimeOffset, long)>(StringComparer.Ordinal);
                    _associationsByTag[tag] = associations;
                }

                DropExpired(associations);
                associations[key] = (expiresAt, stamp);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> ListKeys(string tag)
    {
        if (tag is null)
            throw new ArgumentNullException(nameof(tag));

        lock (_lock)
        {
            if (!_associationsByTag.TryGetValue(tag, out var associations))
                return [];

            DropExpired(associations);

            if (associations.Count == 0)
            {
                _associationsByTag.Remove(tag);
                return [];
            }

            return associations.Keys.ToList();
        }
    }

    /// <inheritdoc />
    public long GetStamp(string key)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        lock (_lock)
        {
            long newest = 0;

            foreach (var associations in _associationsByTag.Values)
            {
                if (associations.TryGetValue(key, out var association) && association.Stamp > newest)
                    newest = association.Stamp;
            }

            return newest;
        }
    }

    /// <inheritdoc />
    public void Forget(string key, long stamp)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        lock (_lock)
        {
            // Removing from a Dictionary while enumerating it is supported, so neither the tag
            // names nor the key sets need a defensive snapshot per call.
            foreach (var entry in _associationsByTag)
            {
                if (entry.Value.TryGetValue(key, out var association) && association.Stamp <= stamp)
                    entry.Value.Remove(key);

                if (entry.Value.Count == 0)
                    _associationsByTag.Remove(entry.Key);
            }
        }
    }

    /// <summary>
    /// The number of tags currently held, used by the tests to observe the sweep.
    /// </summary>
    internal int TagCount
    {
        get
        {
            lock (_lock)
                return _associationsByTag.Count;
        }
    }

    private void SweepIfDue()
    {
        var now = _timeProvider.GetUtcNow();
        if (now < _nextSweep)
            return;

        _nextSweep = now + SweepInterval;

        foreach (var entry in _associationsByTag)
        {
            DropExpired(entry.Value);

            if (entry.Value.Count == 0)
                _associationsByTag.Remove(entry.Key);
        }
    }

    private void DropExpired(Dictionary<string, (DateTimeOffset ExpiresAt, long Stamp)> associations)
    {
        var now = _timeProvider.GetUtcNow();

        foreach (var entry in associations)
        {
            if (entry.Value.ExpiresAt <= now)
                associations.Remove(entry.Key);
        }
    }
}
