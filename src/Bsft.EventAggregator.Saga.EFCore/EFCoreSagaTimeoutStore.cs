using Bsft.EventAggregator.Saga.Timeouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// An <see cref="ISagaTimeoutStore"/> backed by EF Core. Like <see cref="EFCoreSagaStore"/> it is a
/// singleton that resolves a scoped <see cref="ISagaTimeoutDbContext"/> per operation.
/// </summary>
/// <remarks>
/// When a saga persistence window is open, scheduling and cancelling stage their changes into the
/// context that window supplies, so a step's reminders reach the database in the same transaction as
/// its state advance. The dispatcher-side operations — polling, claiming, skipping and counting a
/// failed attempt — never run inside a window and always commit on their own.
/// </remarks>
public sealed class EFCoreSagaTimeoutStore : IEFCoreSagaTimeoutStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISagaDbContextAccessor? _window;

    /// <summary>
    /// Initializes a new instance of the <see cref="EFCoreSagaTimeoutStore"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory used to resolve the scoped DbContext.</param>
    /// <param name="window">
    /// The accessor for an open persistence window, or <c>null</c> when the store always commits on its own.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scopeFactory"/> is null.</exception>
    public EFCoreSagaTimeoutStore(IServiceScopeFactory scopeFactory, ISagaDbContextAccessor? window = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _window = window;
    }

    /// <inheritdoc />
    public bool IsStagingIntoWindow => _window?.Current is not null;

    /// <inheritdoc />
    public async Task ScheduleAsync(SagaTimeout timeout, CancellationToken cancellationToken = default)
    {
        if (timeout is null)
            throw new ArgumentNullException(nameof(timeout));

        // Where the unique pending-name index is in place, a concurrent insert of the same name is
        // rejected on commit and surfaces as a failed step, which the flow host rolls back and the
        // event can retry. Swallowing it would be wrong: the two schedules may carry different due
        // dates, and silently keeping the winner's would drop a reminder the caller believes it set.
        await StageAsync(db => StageScheduleAsync(db, timeout, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a staging action against the context of an open persistence window, leaving the commit to
    /// that window, or against a context of its own, which it then commits.
    /// </summary>
    /// <param name="stage">The action that records the change on the change tracker.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    private async Task StageAsync(Func<ISagaTimeoutDbContext, Task> stage, CancellationToken cancellationToken)
    {
        if (_window?.Current is { } staged)
        {
            await stage(staged).ConfigureAwait(false);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISagaTimeoutDbContext>();

        await stage(db).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task StageScheduleAsync(ISagaTimeoutDbContext db, SagaTimeout timeout, CancellationToken cancellationToken)
    {
        // Re-scheduling under an existing name replaces the pending row instead of stacking a second
        // reminder that nobody could cancel by name any more.
        var existing = await db.SagaTimeouts
            .Where(e => e.DispatchedAt == null
                && e.SagaType == timeout.SagaType
                && e.CorrelationKey == timeout.CorrelationKey
                && e.Name == timeout.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        db.SagaTimeouts.RemoveRange(existing);

        // A timeout staged earlier in this same persistence window is not in the database yet, so the
        // query above cannot see it. Without this sweep a step that schedules the same name twice —
        // "replaces any pending timeout of the same name" — would stage two inserts and fail the whole
        // step on the filtered unique index instead. The local view already excludes the rows just
        // marked deleted, so it only yields what this window added itself.
        var uncommitted = db.SagaTimeouts.Local
            .Where(e => e.DispatchedAt is null
                && e.SagaType == timeout.SagaType
                && e.CorrelationKey == timeout.CorrelationKey
                && e.Name == timeout.Name)
            .ToList();

        db.SagaTimeouts.RemoveRange(uncommitted);
        db.SagaTimeouts.Add(ToEntity(timeout));
    }

    // The guards below are not decoration: a null addresses no row, so the cancel would report
    // success while the timer it was meant to withdraw stays pending and fires later.

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when an argument is <c>null</c>.</exception>
    public Task CancelAsync(string sagaType, string correlationKey, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaType);
        ArgumentNullException.ThrowIfNull(correlationKey);
        ArgumentNullException.ThrowIfNull(name);

        return DeletePendingAsync(
            e => e.SagaType == sagaType && e.CorrelationKey == correlationKey && e.Name == name,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when an argument is <c>null</c>.</exception>
    public Task CancelForStepAsync(string sagaType, string correlationKey, int stepIndex, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaType);
        ArgumentNullException.ThrowIfNull(correlationKey);

        return DeletePendingAsync(
            e => e.SagaType == sagaType && e.CorrelationKey == correlationKey && e.StepIndex == stepIndex,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when an argument is <c>null</c>.</exception>
    public Task CancelAllAsync(string sagaType, string correlationKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaType);
        ArgumentNullException.ThrowIfNull(correlationKey);

        return DeletePendingAsync(
            e => e.SagaType == sagaType && e.CorrelationKey == correlationKey,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SagaTimeout>> ListDueAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISagaTimeoutDbContext>();

        var dueBy = now.UtcDateTime;

        var entities = await db.SagaTimeouts
            .AsNoTracking()
            .Where(e => e.DispatchedAt == null && e.DueAt <= dueBy)
            .OrderBy(e => e.DueAt)
            .ThenBy(e => e.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.ConvertAll(ToTimeout);
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

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISagaTimeoutDbContext>();

        // Confirm the timer still exists and is unclaimed *before* publishing. Skipping this to save
        // a round trip would publish reminders that were cancelled between the poll and now — and a
        // cancellation racing a due timer is the feature's headline case, not an edge case. The
        // spurious event could advance a step the saga is still waiting at.
        var stillPending = await db.SagaTimeouts
            .AsNoTracking()
            .AnyAsync(
                e => e.Id == timeout.Id && e.DispatchedAt == null && e.ConcurrencyToken == timeout.ConcurrencyToken,
                cancellationToken)
            .ConfigureAwait(false);

        if (!stillPending)
            return false;

        await dispatch(timeout, cancellationToken).ConfigureAwait(false);

        var dispatchedAtUtc = dispatchedAt.UtcDateTime;
        var claimedToken = Guid.NewGuid();

        // One conditional statement rather than a tracked save. An affected count of zero means
        // another instance marked it first, so our publish was the duplicate that at-least-once
        // delivery permits.
        var claimed = await db.SagaTimeouts
            .Where(e => e.Id == timeout.Id && e.DispatchedAt == null && e.ConcurrencyToken == timeout.ConcurrencyToken)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.DispatchedAt, dispatchedAtUtc).SetProperty(e => e.ConcurrencyToken, claimedToken),
                cancellationToken)
            .ConfigureAwait(false);

        return claimed == 1;
    }

    /// <inheritdoc />
    public async Task SkipAsync(IReadOnlyCollection<long> ids, DateTimeOffset skippedAt, CancellationToken cancellationToken = default)
    {
        if (ids is null)
            throw new ArgumentNullException(nameof(ids));
        if (ids.Count == 0)
            return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISagaTimeoutDbContext>();

        var skippedAtUtc = skippedAt.UtcDateTime;

        await db.SagaTimeouts
            .Where(e => ids.Contains(e.Id) && e.DispatchedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.DispatchedAt, skippedAtUtc), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordFailedAttemptAsync(long id, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISagaTimeoutDbContext>();

        await db.SagaTimeouts
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.AttemptCount, e => e.AttemptCount + 1), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeadLetterAsync(long id, DateTimeOffset deadLetteredAt, string reason, CancellationToken cancellationToken = default)
    {
        // The reason comes from an exception message, whose length nothing bounds; the configuration
        // that owns the column width cuts it to fit rather than letting the write fail.
        var recordedReason = SagaTimeoutEntityConfiguration.FitReason(reason);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISagaTimeoutDbContext>();

        var retiredAt = deadLetteredAt.UtcDateTime;

        // Guarded on DispatchedAt like SkipAsync, and stamping it as well: that is what takes the row
        // out of the due query, out of the filtered pending-name index — so the name can be scheduled
        // again — and out of reach of a cancellation. DeadLetteredAt keeps the retirement legible
        // beside the timers that were dispatched or skipped normally.
        await db.SagaTimeouts
            .Where(e => e.Id == id && e.DispatchedAt == null)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.AttemptCount, e => e.AttemptCount + 1)
                    .SetProperty(e => e.DeadLetteredAt, retiredAt)
                    .SetProperty(e => e.DeadLetterReason, recordedReason)
                    .SetProperty(e => e.DispatchedAt, retiredAt),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DeletePendingAsync(
        System.Linq.Expressions.Expression<Func<SagaTimeoutEntity, bool>> predicate,
        CancellationToken cancellationToken)
    {
        if (_window?.Current is { } staged)
        {
            // ExecuteDelete runs its own statement outside the change tracker and would commit
            // immediately — exactly the split second write this window exists to remove. Inside one,
            // the rows are loaded and marked deleted so they travel with the state advance.
            var persisted = await staged.SagaTimeouts
                .Where(e => e.DispatchedAt == null)
                .Where(predicate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            staged.SagaTimeouts.RemoveRange(persisted);

            // A timer scheduled earlier in this same window is not in the database yet, so the query
            // above cannot see it. A step that schedules a reminder and then cancels it must still
            // end up with nothing pending, so the change tracker is swept as well.
            var matches = predicate.Compile();
            var uncommitted = staged.SagaTimeouts.Local
                .Where(e => e.DispatchedAt is null && matches(e))
                .ToList();

            staged.SagaTimeouts.RemoveRange(uncommitted);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISagaTimeoutDbContext>();

        // One statement, not load-then-delete: cancelling runs on every step transition and every
        // saga completion, so a second round trip here is paid on the hottest path of this store.
        await db.SagaTimeouts
            .Where(e => e.DispatchedAt == null)
            .Where(predicate)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static SagaTimeoutEntity ToEntity(SagaTimeout timeout) => new()
    {
        SagaType = timeout.SagaType,
        CorrelationKey = timeout.CorrelationKey,
        Name = timeout.Name,
        StepIndex = timeout.StepIndex,
        DueAt = timeout.DueAt.UtcDateTime,
        PayloadJson = timeout.PayloadJson,
        PayloadTypeName = timeout.PayloadTypeName,
        CorrelationId = timeout.CorrelationId,
        AttemptCount = timeout.AttemptCount,
        DispatchedAt = timeout.DispatchedAt?.UtcDateTime,
        DeadLetteredAt = timeout.DeadLetteredAt?.UtcDateTime,
        DeadLetterReason = timeout.DeadLetterReason,
        ConcurrencyToken = timeout.ConcurrencyToken,
    };

    private static SagaTimeout ToTimeout(SagaTimeoutEntity entity) => new()
    {
        Id = entity.Id,
        SagaType = entity.SagaType,
        CorrelationKey = entity.CorrelationKey,
        Name = entity.Name,
        StepIndex = entity.StepIndex,
        DueAt = new DateTimeOffset(DateTime.SpecifyKind(entity.DueAt, DateTimeKind.Utc)),
        PayloadJson = entity.PayloadJson,
        PayloadTypeName = entity.PayloadTypeName,
        CorrelationId = entity.CorrelationId,
        AttemptCount = entity.AttemptCount,
        DispatchedAt = entity.DispatchedAt is { } dispatched
            ? new DateTimeOffset(DateTime.SpecifyKind(dispatched, DateTimeKind.Utc))
            : null,
        DeadLetteredAt = entity.DeadLetteredAt is { } deadLettered
            ? new DateTimeOffset(DateTime.SpecifyKind(deadLettered, DateTimeKind.Utc))
            : null,
        DeadLetterReason = entity.DeadLetterReason,
        ConcurrencyToken = entity.ConcurrencyToken,
    };
}
