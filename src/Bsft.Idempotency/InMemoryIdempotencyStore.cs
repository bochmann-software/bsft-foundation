using System.Collections.Concurrent;

namespace Bsft.Idempotency;

/// <summary>
/// The default store, which remembers keys in the memory of one process.
/// </summary>
/// <remarks>
/// <para>
/// It closes the case this package exists for — the double-clicked submit button, the mobile client
/// retrying a request whose response never arrived — on a single-instance deployment, and it is
/// deliberately <em>not</em> enough for more than one: two replicas have two memories, so a
/// duplicate that lands on the other one runs. <c>Bsft.Idempotency.EFCore</c> is the shared store,
/// and it replaces this one rather than adding to it.
/// </para>
/// <para>
/// Records are swept lazily rather than by a timer, because a process that is serving no requests
/// has nothing to forget and a background timer would be a thread doing nothing on every host that
/// never sets a key.
/// </para>
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    /// <summary>How often expired records are swept out, at most.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<(string Scope, string Key), Entry> _records = new();
    private readonly TimeProvider _timeProvider;
    private readonly object _sweepGate = new();

    private DateTimeOffset _sweptAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryIdempotencyStore"/> class.
    /// </summary>
    /// <param name="timeProvider">The clock every expiry is read from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    public InMemoryIdempotencyStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _sweptAt = _timeProvider.GetUtcNow();
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    public Task<IdempotencyBeginResult> BeginAsync(
        IdempotencyReservationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _timeProvider.GetUtcNow();

        Sweep(now);

        return Task.FromResult(Claim(request, now));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    public Task CompleteAsync(IdempotencyCompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var identity = (request.Scope, request.Key);

        // Only our own reservation is completed. If the lease lapsed and another request claimed the
        // key in the meantime, that request owns it now — which the fingerprint is what proves, since
        // the state alone would also match the reservation the other request is still holding.
        if (IsOwned(identity, request.Fingerprint, out var existing))
        {
            _records.TryUpdate(
                identity,
                existing with
                {
                    State = IdempotencyRecordState.Completed,
                    ExpiresAt = request.ExpiresAt,
                    Response = request.Response,
                },
                existing);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public Task ReleaseAsync(
        string scope, string key, string fingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(fingerprint);

        var identity = (scope, key);

        if (IsOwned(identity, fingerprint, out var existing))
            _records.TryRemove(new KeyValuePair<(string, string), Entry>(identity, existing));

        return Task.CompletedTask;
    }

    /// <summary>Reports whether the standing record is the reservation this request made.</summary>
    /// <param name="identity">The scope and key.</param>
    /// <param name="fingerprint">The digest the request reserved with.</param>
    /// <param name="existing">The standing record, when there is one that matches.</param>
    /// <returns><see langword="true"/> when the record may be completed or released by this request.</returns>
    private bool IsOwned(
        (string Scope, string Key) identity, string fingerprint, out Entry existing)
    {
        return _records.TryGetValue(identity, out existing!)
            && existing.State == IdempotencyRecordState.Reserved
            && string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal);
    }

    /// <summary>Claims the key, or works out what the standing record says instead.</summary>
    /// <param name="request">What is being reserved.</param>
    /// <param name="now">The instant every expiry is judged against.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// The loop is the deduplication. A read followed by a write would let two concurrent requests
    /// both find nothing and both proceed; only the conditional add and the compare-and-swap below
    /// guarantee that exactly one of them is told it started.
    /// </remarks>
    private IdempotencyBeginResult Claim(IdempotencyReservationRequest request, DateTimeOffset now)
    {
        var identity = (request.Scope, request.Key);
        var reservation = new Entry(
            IdempotencyRecordState.Reserved, request.Fingerprint, request.LeaseExpiresAt, Response: null);

        while (true)
        {
            if (!_records.TryGetValue(identity, out var existing))
            {
                if (_records.TryAdd(identity, reservation))
                    return new IdempotencyBeginResult(IdempotencyBeginOutcome.Started);

                continue;
            }

            if (!HasExpired(existing, now))
                return Describe(existing, request.Fingerprint);

            // A completed record past its retention, or a reservation past its lease, decides
            // nothing: the key runs again, which is exactly what both expiries promise.
            if (_records.TryUpdate(identity, reservation, existing))
                return new IdempotencyBeginResult(IdempotencyBeginOutcome.Started);
        }
    }

    /// <summary>Turns a standing record into the answer the caller gets.</summary>
    /// <param name="existing">The record that is in the way.</param>
    /// <param name="fingerprint">The fingerprint of the request asking.</param>
    /// <returns>What happened.</returns>
    private static IdempotencyBeginResult Describe(Entry existing, string fingerprint)
    {
        if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            return new IdempotencyBeginResult(IdempotencyBeginOutcome.FingerprintMismatch);

        return existing is { State: IdempotencyRecordState.Completed, Response: { } response }
            ? new IdempotencyBeginResult(IdempotencyBeginOutcome.Replayed, response)
            : new IdempotencyBeginResult(
                IdempotencyBeginOutcome.InProgress, LeaseExpiresAt: existing.ExpiresAt);
    }

    /// <summary>Reports whether a record has stopped deciding anything.</summary>
    /// <param name="entry">The record.</param>
    /// <param name="now">The instant to judge against.</param>
    /// <returns><see langword="true"/> when the record may be replaced.</returns>
    private static bool HasExpired(Entry entry, DateTimeOffset now) => entry.ExpiresAt <= now;

    /// <summary>Drops expired records, at most once per <see cref="SweepInterval"/>.</summary>
    /// <param name="now">The instant to judge against.</param>
    /// <remarks>
    /// Walking the whole map on every request would make a protected endpoint's cost a function of
    /// its own recent traffic.
    /// </remarks>
    private void Sweep(DateTimeOffset now)
    {
        lock (_sweepGate)
        {
            if (now - _sweptAt < SweepInterval)
                return;

            _sweptAt = now;
        }

        // Enumerated directly rather than through a copy: a ConcurrentDictionary's enumerator
        // tolerates removal while it runs, which is the only reason the copy was there.
        foreach (var entry in _records)
        {
            if (HasExpired(entry.Value, now))
                _records.TryRemove(entry);
        }
    }

    /// <summary>
    /// One remembered key: what state it is in, which request it names, when it stops deciding
    /// anything, and the response a replay serves.
    /// </summary>
    /// <param name="State">Whether the request is running or finished.</param>
    /// <param name="Fingerprint">The request the key names.</param>
    /// <param name="ExpiresAt">
    /// The lease while reserved, the retention once completed. One member rather than two, because
    /// the two never apply at once and a record with both would have a state its own fields deny.
    /// </param>
    /// <param name="Response">The stored response, present only once completed.</param>
    private sealed record Entry(
        IdempotencyRecordState State,
        string Fingerprint,
        DateTimeOffset ExpiresAt,
        IdempotencyResponseSnapshot? Response);
}
