namespace Bsft.Core.Concurrency;

/// <summary>
/// The default <see cref="IKeyedGate{TKey}"/>: one first-in-first-out gate per key, kept only while
/// some caller holds it or waits for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Keys.</b> Which keys are the same is the comparer's decision, and a key is passed to it as it
/// is: a caller that wants paths compared without a trailing separator normalises them before the
/// call. A key that is a mutable object must not change while it is held or awaited.
/// </para>
/// <para>
/// <b>No leaked entries.</b> Every caller that waits for or holds a key counts as a user of its
/// entry. The entry is removed when the last user leaves, whether it released the gate or was
/// cancelled while waiting, so a gate that served any number of distinct keys is empty again once
/// they are all released (<see cref="TrackedKeys"/>).
/// </para>
/// <para>
/// <b>Fairness and cancellation</b> are those of <see cref="SlotPool"/>, which holds the gate of one
/// key: callers for one key are admitted in the order they asked, an already cancelled token yields a
/// cancelled task without tracking the key, a waiter cancelled in the queue is skipped, and a handle
/// disposed twice frees the gate once.
/// </para>
/// </remarks>
/// <typeparam name="TKey">The key that names what is guarded.</typeparam>
public sealed class KeyedGate<TKey> : IKeyedGate<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> _entries;
    private readonly object _lock = new();

    /// <summary>Initializes a new instance of the <see cref="KeyedGate{TKey}"/> class.</summary>
    /// <param name="comparer">
    /// Decides which keys are the same; <see langword="null"/> for the default comparer of
    /// <typeparamref name="TKey"/>.
    /// </param>
    public KeyedGate(IEqualityComparer<TKey>? comparer = null) => _entries = new Dictionary<TKey, Entry>(comparer);

    /// <summary>Gets how many keys are held or waited for right now; zero when the gate is idle.</summary>
    public int TrackedKeys
    {
        get
        {
            lock (_lock)
                return _entries.Count;
        }
    }

    /// <inheritdoc />
    public async Task<IDisposable> EnterAsync(TKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = Acquire(key);
        IDisposable slot;
        try
        {
            slot = await entry.Gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled while waiting: this caller no longer needs the entry.
            Return(key, entry);
            throw;
        }

        return new Held(this, key, entry, slot);
    }

    private Entry Acquire(TKey key)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var entry))
                _entries.Add(key, entry = new Entry());

            entry.Users++;
            return entry;
        }
    }

    private void Return(TKey key, Entry entry)
    {
        lock (_lock)
        {
            if (--entry.Users == 0)
                _entries.Remove(key);
        }
    }

    private sealed class Entry
    {
        internal SlotPool Gate { get; } = new(1);

        internal int Users { get; set; }
    }

    private sealed class Held(KeyedGate<TKey> owner, TKey key, Entry entry, IDisposable slot) : IDisposable
    {
        private int _released;

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            slot.Dispose();
            owner.Return(key, entry);
        }
    }
}
