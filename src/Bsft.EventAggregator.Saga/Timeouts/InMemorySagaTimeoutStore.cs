using System.Collections.Concurrent;

namespace Bsft.EventAggregator.Saga.Timeouts;

/// <summary>
/// In-memory <see cref="ISagaTimeoutStore"/> used when no persistent provider is configured.
/// Timeouts are lost when the process ends, so this is for tests and single-process demos only —
/// a saga that waits days for a reminder needs the EF Core store.
/// </summary>
public sealed class InMemorySagaTimeoutStore : ISagaTimeoutStore
{
    private readonly ConcurrentDictionary<long, SagaTimeout> _timeouts = new();
    private readonly object _claimGate = new();
    private long _nextId;

    /// <inheritdoc />
    public Task ScheduleAsync(SagaTimeout timeout, CancellationToken cancellationToken = default)
    {
        if (timeout is null)
            throw new ArgumentNullException(nameof(timeout));

        // Re-scheduling a timeout of the same name replaces the pending one rather than adding a
        // second entry. Unlike the persistent store this is convention, not a constraint: two
        // concurrent schedules of the same name can both pass the removal. Acceptable here, because
        // this store is for tests and single-process demos.
        DeletePending(t =>
            string.Equals(t.SagaType, timeout.SagaType, StringComparison.Ordinal)
            && string.Equals(t.CorrelationKey, timeout.CorrelationKey, StringComparison.Ordinal)
            && string.Equals(t.Name, timeout.Name, StringComparison.Ordinal));

        timeout.Id = Interlocked.Increment(ref _nextId);
        _timeouts[timeout.Id] = timeout;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CancelAsync(string sagaType, string correlationKey, string name, CancellationToken cancellationToken = default)
    {
        DeletePending(t =>
            string.Equals(t.SagaType, sagaType, StringComparison.Ordinal)
            && string.Equals(t.CorrelationKey, correlationKey, StringComparison.Ordinal)
            && string.Equals(t.Name, name, StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CancelForStepAsync(string sagaType, string correlationKey, int stepIndex, CancellationToken cancellationToken = default)
    {
        DeletePending(t =>
            string.Equals(t.SagaType, sagaType, StringComparison.Ordinal)
            && string.Equals(t.CorrelationKey, correlationKey, StringComparison.Ordinal)
            && t.StepIndex == stepIndex);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CancelAllAsync(string sagaType, string correlationKey, CancellationToken cancellationToken = default)
    {
        DeletePending(t =>
            string.Equals(t.SagaType, sagaType, StringComparison.Ordinal)
            && string.Equals(t.CorrelationKey, correlationKey, StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SagaTimeout>> ListDueAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SagaTimeout> due = _timeouts.Values
            .Where(t => t.DispatchedAt is null && t.DueAt <= now)
            .OrderBy(t => t.DueAt)
            .ThenBy(t => t.Id)
            .Take(batchSize)
            .ToList();

        return Task.FromResult(due);
    }

    /// <inheritdoc />
    public async Task<bool> TryDispatchAsync(
        SagaTimeout timeout,
        DateTimeOffset dispatchedAt,
        Func<SagaTimeout, CancellationToken, Task> dispatch,
        CancellationToken cancellationToken = default)
    {
        if (timeout is null)
            throw new ArgumentNullException(nameof(timeout));
        if (dispatch is null)
            throw new ArgumentNullException(nameof(dispatch));

        if (!_timeouts.TryGetValue(timeout.Id, out var stored) || stored.DispatchedAt is not null)
            return false;

        await dispatch(stored, cancellationToken).ConfigureAwait(false);

        // Claim after the dispatch, exactly like the persistent store does with its concurrency
        // token: whoever marks first wins, the loser's publish was a duplicate. Cancelling removes
        // the entry rather than stamping it, so presence has to be re-checked too — otherwise a
        // cancel landing during the publish would still be reported as a successful dispatch.
        lock (_claimGate)
        {
            if (!_timeouts.TryGetValue(timeout.Id, out var current) || !ReferenceEquals(current, stored))
                return false;

            if (stored.DispatchedAt is not null)
                return false;

            stored.DispatchedAt = dispatchedAt;
            stored.ConcurrencyToken = Guid.NewGuid();
            return true;
        }
    }

    /// <inheritdoc />
    public Task SkipAsync(IReadOnlyCollection<long> ids, DateTimeOffset skippedAt, CancellationToken cancellationToken = default)
    {
        if (ids is null)
            throw new ArgumentNullException(nameof(ids));

        // Only a still-pending timeout may be retired, matching the persistent store's
        // `DispatchedAt == null` filter. Without the guard a skip racing a dispatch would overwrite
        // the real dispatch timestamp and turn a fired timer into a skipped one.
        lock (_claimGate)
        {
            foreach (var id in ids)
            {
                if (_timeouts.TryGetValue(id, out var timeout) && timeout.DispatchedAt is null)
                    timeout.DispatchedAt = skippedAt;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordFailedAttemptAsync(long id, CancellationToken cancellationToken = default)
    {
        if (_timeouts.TryGetValue(id, out var timeout))
            timeout.AttemptCount++;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeadLetterAsync(long id, DateTimeOffset deadLetteredAt, string reason, CancellationToken cancellationToken = default)
    {
        if (reason is null)
            throw new ArgumentNullException(nameof(reason));

        // Under the same gate and the same still-pending guard as SkipAsync: retiring a timer the
        // dispatcher claimed a moment ago would turn a fired timeout into a failed one. Stamping
        // DispatchedAt as well is what takes the row out of the due set, out of the pending-name
        // uniqueness rule and out of reach of a cancellation, exactly as a skip does.
        lock (_claimGate)
        {
            if (_timeouts.TryGetValue(id, out var timeout) && timeout.DispatchedAt is null)
            {
                timeout.AttemptCount++;
                timeout.DeadLetteredAt = deadLetteredAt;
                timeout.DeadLetterReason = reason;
                timeout.DispatchedAt = deadLetteredAt;
            }
        }

        return Task.CompletedTask;
    }

    private void DeletePending(Func<SagaTimeout, bool> predicate)
    {
        foreach (var entry in _timeouts.Where(e => e.Value.DispatchedAt is null && predicate(e.Value)))
        {
            _timeouts.TryRemove(entry.Key, out _);
        }
    }
}
