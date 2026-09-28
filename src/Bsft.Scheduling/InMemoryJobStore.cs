using System.Collections.Concurrent;

namespace Bsft.Scheduling;

/// <summary>
/// In-memory <see cref="IJobStore"/> used when no persistent store is configured. Runs are lost when
/// the process ends, so a delayed job scheduled for tomorrow never fires after a restart — use the
/// EF Core store for anything that has to survive one.
/// </summary>
/// <remarks>
/// Reads hand out copies rather than the stored instances, matching what a database-backed store
/// necessarily does. Without that, a caller mutating a returned run would silently rewrite the store
/// and the two implementations would behave differently under the same tests.
/// </remarks>
public sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<long, JobRun> _runs = new();
    private readonly object _gate = new();
    private long _nextId;

    /// <inheritdoc />
    public Task<long> EnqueueAsync(JobRun run, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        lock (_gate)
        {
            return Task.FromResult(Insert(run));
        }
    }

    /// <inheritdoc />
    public Task<bool> TryEnqueueRecurringAsync(JobRun run, JobOverlapPolicy overlapPolicy, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        // The check and the insert share the gate so two instances seeding the same recurring job at
        // startup cannot both pass the check and create a duplicate occurrence.
        lock (_gate)
        {
            if (_runs.Values.Any(r =>
                    IsLive(r, overlapPolicy) && string.Equals(r.JobName, run.JobName, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }

            Insert(run);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobRun>> ListClaimableAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        // The gate covers the read, not just the writes. Claim, settle and retry mutate the stored
        // instances field by field, and a copy taken beside them would be free to catch a run
        // half-updated — a status of Running paired with the lease of the previous attempt, or a
        // DateTimeOffset read while it was being written. A database-backed store never hands out
        // such a row, and this one must not either.
        lock (_gate)
        {
            IReadOnlyList<JobRun> claimable = _runs.Values
                .Where(r => IsClaimable(r, now))
                .OrderBy(r => r.DueAt)
                .ThenBy(r => r.Id)
                .Take(batchSize)
                .Select(r => r.Copy())
                .ToList();

            return Task.FromResult(claimable);
        }
    }

    /// <inheritdoc />
    public Task<JobRun?> ClaimAsync(JobRun run, string owner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));
        if (owner is null)
            throw new ArgumentNullException(nameof(owner));

        lock (_gate)
        {
            if (!_runs.TryGetValue(run.Id, out var stored)
                || stored.ConcurrencyToken != run.ConcurrencyToken
                || !IsClaimable(stored, now))
            {
                return Task.FromResult<JobRun?>(null);
            }

            stored.Status = JobRunStatus.Running;
            stored.AttemptCount++;
            stored.LeaseOwner = owner;
            stored.LeaseExpiresAt = leaseExpiresAt;
            stored.ConcurrencyToken = Guid.NewGuid();

            return Task.FromResult<JobRun?>(stored.Copy());
        }
    }

    /// <inheritdoc />
    public Task SettleAsync(JobRun run, JobRunStatus status, DateTimeOffset settledAt, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        lock (_gate)
        {
            if (!TryGetHeld(run, out var stored))
                return Task.CompletedTask;

            stored.Status = status;
            stored.SettledAt = settledAt;
            stored.LeaseOwner = null;
            stored.LeaseExpiresAt = null;
            stored.ConcurrencyToken = Guid.NewGuid();
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task RetryAsync(JobRun run, DateTimeOffset dueAt, CancellationToken cancellationToken = default)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        lock (_gate)
        {
            if (!TryGetHeld(run, out var stored))
                return Task.CompletedTask;

            var keepsSlot = stored.IsRecurring && !HasSuccessor(stored);

            stored.Status = JobRunStatus.Pending;
            stored.DueAt = dueAt;
            stored.IsRecurring = keepsSlot;
            stored.LeaseOwner = null;
            stored.LeaseExpiresAt = null;
            stored.ConcurrencyToken = Guid.NewGuid();
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<bool> CancelAsync(long id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(id, out var stored) || stored.Status != JobRunStatus.Pending)
                return Task.FromResult(false);

            return Task.FromResult(_runs.TryRemove(id, out _));
        }
    }

    /// <inheritdoc />
    public Task<JobRun?> FindAsync(long id, CancellationToken cancellationToken = default)
    {
        // Under the gate for the same reason as the poll: the copy has to be taken between the
        // mutations of a claim or a settle, never in the middle of one.
        lock (_gate)
        {
            return Task.FromResult(_runs.TryGetValue(id, out var stored) ? stored.Copy() : null);
        }
    }

    /// <summary>
    /// Determines whether a run occupies the single-occurrence slot of its recurring job. A pending
    /// occurrence always does. A running one does so only under <see cref="JobOverlapPolicy.Forbid"/>,
    /// which is what stops a successor from being created while the job is still working.
    /// </summary>
    /// <remarks>
    /// Only recurring rows count. A one-shot run enqueued under the same job name is ordinary work,
    /// and letting it occupy the slot would stop the recurring schedule dead — the persistent store's
    /// filtered index draws the same line.
    /// </remarks>
    private static bool IsLive(JobRun run, JobOverlapPolicy overlapPolicy) =>
        run.IsRecurring
        && (run.Status == JobRunStatus.Pending
            || (overlapPolicy == JobOverlapPolicy.Forbid && run.Status == JobRunStatus.Running));

    /// <summary>
    /// Determines whether the recurring job's single pending-occurrence slot is already held by a run
    /// other than this one — its successor, created before the work started under
    /// <see cref="JobOverlapPolicy.Allow"/>.
    /// </summary>
    private bool HasSuccessor(JobRun run) =>
        _runs.Values.Any(r =>
            r.Id != run.Id
            && r.IsRecurring
            && r.Status == JobRunStatus.Pending
            && string.Equals(r.JobName, run.JobName, StringComparison.Ordinal));

    /// <summary>
    /// Determines whether a run may be claimed: either it is pending and due, or it is running under
    /// a lease that has lapsed because the owning instance never came back.
    /// </summary>
    private static bool IsClaimable(JobRun run, DateTimeOffset now) =>
        (run.Status == JobRunStatus.Pending && run.DueAt <= now)
        || (run.Status == JobRunStatus.Running && run.LeaseExpiresAt <= now);

    private long Insert(JobRun run)
    {
        var id = Interlocked.Increment(ref _nextId);
        var stored = run.Copy();
        stored.Id = id;
        stored.ConcurrencyToken = Guid.NewGuid();
        _runs[id] = stored;

        // The caller's instance is stamped too, so it can be enqueued and then cancelled by id
        // without a round-trip through the store.
        run.Id = id;
        run.ConcurrencyToken = stored.ConcurrencyToken;
        return id;
    }

    /// <summary>
    /// Resolves the stored run behind a claimed one, rejecting a token that no longer matches — the
    /// lease was lost to another instance, so this caller must not overwrite its outcome.
    /// </summary>
    private bool TryGetHeld(JobRun run, out JobRun stored) =>
        _runs.TryGetValue(run.Id, out stored!) && stored.ConcurrencyToken == run.ConcurrencyToken;

}
