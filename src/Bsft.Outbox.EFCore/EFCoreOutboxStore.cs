using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Outbox.EFCore;

/// <summary>
/// The one EF Core implementation of the outbox lease protocol, generic over the row it leases. A
/// second outbox in a host is a second entity and a second registration, never a second protocol.
/// </summary>
/// <typeparam name="TEntity">The entity that holds the rows of this outbox.</typeparam>
/// <typeparam name="TPayload">What those rows carry.</typeparam>
/// <remarks>
/// <para>
/// It is a singleton that resolves a scoped context per operation, so it can serve the long-running
/// drainer without holding a context open between passes.
/// </para>
/// <para>
/// Every state transition is a single conditional statement rather than a load-modify-save. The
/// condition carries the guard — pending, and leased by this caller — so the database decides the race
/// between two application instances, and an affected count of zero is the answer that this caller
/// lost it.
/// </para>
/// </remarks>
public sealed class EFCoreOutboxStore<TEntity, TPayload> : IOutboxStore<TPayload>
    where TEntity : OutboxRow
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutboxRowSource<TEntity, TPayload> _rows;

    /// <summary>
    /// Initializes a new instance of the <see cref="EFCoreOutboxStore{TEntity, TPayload}"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory used to resolve the scoped context.</param>
    /// <param name="rows">Where this outbox's rows live and how one is read.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public EFCoreOutboxStore(IServiceScopeFactory scopeFactory, IOutboxRowSource<TEntity, TPayload> rows)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeasedOutboxRow<TPayload>>> LeaseAsync(
        DateTimeOffset now, TimeSpan leaseDuration, int batchSize, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var set = _rows.Rows(scope.ServiceProvider);

        var nowUtc = now.UtcDateTime;
        var leasedUntil = now.Add(leaseDuration).UtcDateTime;

        // The whole row is read in the one poll query, not just its id, so a claimed row does not have
        // to be read a second time — which on a batch a hundred rows wide would double the round trips.
        // What makes that safe is the claim below: it carries every mutable column of the row into its
        // condition, so a claim only wins while the values read here still hold.
        var candidates = await set
            .AsNoTracking()
            .Where(Claimable(nowUtc))
            .OrderBy(e => e.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var claimed = new List<LeasedOutboxRow<TPayload>>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var leaseToken = Guid.NewGuid();

            // The previous token is part of the condition, so of two drainers that both saw the row
            // unclaimed only the first to reach the database writes the new lease; the second updates
            // nothing and moves on rather than applying the same effect in parallel. The attempt count
            // is in the condition for a subtler reason: another instance abandoning this row raises it
            // without touching the lease token, and the drainer decides on that count when a row has
            // failed often enough to be retired. Claiming on a count read before that increment would
            // hand out a stale one and buy the row extra attempts, so the claim is dropped instead and
            // the next pass reads the row afresh.
            var affected = await set
                .Where(Claimable(nowUtc))
                .Where(e => e.Id == candidate.Id
                    && e.LeaseToken == candidate.LeaseToken
                    && e.AttemptCount == candidate.AttemptCount)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(e => e.LeasedUntil, leasedUntil)
                        .SetProperty(e => e.LeaseToken, leaseToken),
                    cancellationToken)
                .ConfigureAwait(false);

            if (affected == 1)
            {
                claimed.Add(new LeasedOutboxRow<TPayload>(
                    candidate.Id, _rows.PayloadOf(candidate), candidate.AttemptCount, leaseToken));
            }
        }

        return claimed;
    }

    /// <summary>
    /// What "this row may be handed to a drainer now" means: not applied, not retired, not leased by
    /// anyone whose lease still holds, and not being held back for a later attempt.
    /// </summary>
    /// <param name="nowUtc">The instant both the lease and the retry instant are compared against.</param>
    /// <returns>The predicate, as an expression the provider translates into the WHERE clause.</returns>
    /// <remarks>
    /// It is one expression used twice, by the poll query and by the conditional claim that follows it,
    /// and that is the point: two copies of this condition could drift apart, and a claim looser than
    /// the query it filtered would hand out a row the query had already decided against.
    /// </remarks>
    private static Expression<Func<TEntity, bool>> Claimable(DateTime nowUtc) =>
        e => e.ProcessedAt == null
            && e.DeadLetteredAt == null
            && (e.LeasedUntil == null || e.LeasedUntil <= nowUtc)
            && (e.NextAttemptAt == null || e.NextAttemptAt <= nowUtc);

    /// <inheritdoc />
    public async Task<bool> TryMarkProcessedAsync(
        long id, Guid leaseToken, DateTimeOffset processedAt, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var set = _rows.Rows(scope.ServiceProvider);

        var processedAtUtc = processedAt.UtcDateTime;

        var affected = await set
            .Where(e => e.Id == id && e.ProcessedAt == null && e.LeaseToken == leaseToken)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.ProcessedAt, processedAtUtc)
                    .SetProperty(e => e.LeasedUntil, (DateTime?)null),
                cancellationToken)
            .ConfigureAwait(false);

        return affected == 1;
    }

    /// <inheritdoc />
    public async Task AbandonAsync(
        long id, Guid leaseToken, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var set = _rows.Rows(scope.ServiceProvider);

        var nextAttemptAtUtc = nextAttemptAt.UtcDateTime;

        // The lease is cleared instead of left to expire, and how long the row waits is said by
        // NextAttemptAt rather than by the lease duration: the two answer different questions, and
        // conflating them would tie the backoff to how long a drainer may hold a row. The attempt is
        // counted, which is both the diagnostic and what the schedule grows on.
        await set
            .Where(e => e.Id == id && e.ProcessedAt == null && e.LeaseToken == leaseToken)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.LeasedUntil, (DateTime?)null)
                    .SetProperty(e => e.NextAttemptAt, nextAttemptAtUtc)
                    .SetProperty(e => e.AttemptCount, e => e.AttemptCount + 1),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeadLetterAsync(
        long id, Guid leaseToken, DateTimeOffset deadLetteredAt, string reason, CancellationToken cancellationToken = default)
    {
        if (reason is null)
            throw new ArgumentNullException(nameof(reason));

        using var scope = _scopeFactory.CreateScope();
        var set = _rows.Rows(scope.ServiceProvider);

        var deadLetteredAtUtc = deadLetteredAt.UtcDateTime;

        // The reason comes from an exception message, whose length nothing bounds; it is cut to the
        // column width here rather than failing the very write that records why the attempt failed.
        var recordedReason = Truncate(reason);

        await set
            .Where(e => e.Id == id && e.ProcessedAt == null && e.LeaseToken == leaseToken)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.DeadLetteredAt, deadLetteredAtUtc)
                    .SetProperty(e => e.FailureReason, recordedReason)
                    .SetProperty(e => e.LeasedUntil, (DateTime?)null)
                    .SetProperty(e => e.AttemptCount, e => e.AttemptCount + 1),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Cuts the recorded failure to the column width without splitting a surrogate pair. A cut through
    /// a pair leaves an unpaired surrogate, which a provider that validates its text encoding rejects —
    /// and the write being rejected here is the one that ends the row's retry loop.
    /// </summary>
    private string Truncate(string reason)
    {
        if (reason.Length <= _rows.FailureReasonMaxLength)
            return reason;

        var length = _rows.FailureReasonMaxLength;

        if (char.IsHighSurrogate(reason[length - 1]))
            length--;

        return reason[..length];
    }
}
