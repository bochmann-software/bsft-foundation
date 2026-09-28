using System.Text.Json;
using Bsft.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// An <see cref="ISagaStore"/> implementation backed by EF Core.
/// Uses <see cref="IServiceScopeFactory"/> to resolve a scoped <see cref="ISagaDbContext"/>
/// on each operation, since the store is registered as a singleton while the DbContext is scoped.
/// </summary>
/// <remarks>
/// When a saga persistence window is open, the writes are staged into the context that window
/// supplies instead, so that the state advance of a step and the step's timeout writes reach the
/// database in one transaction. Reads never take part: they run before the window is opened.
/// </remarks>
public sealed class EFCoreSagaStore : IEFCoreSagaStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ISagaDbContextAccessor? _window;

    /// <summary>
    /// Initializes a new instance of the <see cref="EFCoreSagaStore"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory used to resolve scoped dependencies.</param>
    /// <param name="timeProvider">The time provider used for timestamping saga state changes.</param>
    /// <param name="window">
    /// The accessor for an open persistence window, or <c>null</c> when the store always commits on
    /// its own — which is the case for the state-only provider, whose timeouts are not persisted.
    /// </param>
    public EFCoreSagaStore(
        IServiceScopeFactory scopeFactory,
        TimeProvider? timeProvider = null,
        ISagaDbContextAccessor? window = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _window = window;
    }

    /// <inheritdoc />
    public async Task<SagaInstance<TState>?> FindAsync<TState>(string sagaType, string correlationKey, CancellationToken cancellationToken = default)
    {
        if (sagaType is null) throw new ArgumentNullException(nameof(sagaType));
        if (correlationKey is null) throw new ArgumentNullException(nameof(correlationKey));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISagaDbContext>();

        var entity = await db.SagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.SagaType == sagaType && e.CorrelationKey == correlationKey, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
            return null;

        // Resolved through the shared resolver rather than through Type.GetType, which would load an
        // assembly named in the stored string from disk. The row is untrusted input, so a tampered
        // name may at worst reach a type the process already carries.
        var stateType = SagaPersistedTypeResolver.Resolve(entity.StateTypeName);

        // The stored type name comes from the database. Only deserialize into a type that is actually
        // a TState, so a tampered StateTypeName cannot drive instantiation of an arbitrary type.
        if (stateType is null || !typeof(TState).IsAssignableFrom(stateType))
            return null;

        var state = (TState?)JsonSerializer.Deserialize(entity.StateJson, stateType);
        if (state is null)
            return null;

        return new SagaInstance<TState>(entity.SagaType, entity.CorrelationKey, state)
        {
            CurrentStepIndex = entity.CurrentStepIndex,
            IsComplete = entity.IsComplete,

            // Captured so a later SaveAsync can tell whether the row was advanced by another writer
            // between this read and that write.
            Version = entity.ConcurrencyToken
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// A true optimistic-concurrency save that refuses a stale write rather than reconciling it. The
    /// token the caller loaded is carried on <see cref="SagaInstance{TState}.Version"/>; the save
    /// compares it against the row it is about to write and rolls the row's token forward, so a
    /// snapshot another writer has since advanced is refused instead of silently overwriting that
    /// writer's step. The refusal is a <see cref="PreconditionFailedException"/> — the same conflict
    /// <c>EFCoreFeatureFlagStore</c> and <c>EFCoreSubscriptionStore</c> raise — and it propagates: the
    /// store neither mutates <paramref name="instance"/> nor retries. The saga engine reloads fresh
    /// state and re-handles the event, so a step body runs at least once under genuine concurrency.
    /// On the windowed path the guarded <c>SaveChangesAsync</c> runs later in the persistence window's
    /// commit, which surfaces the very same refusal.
    /// </remarks>
    /// <exception cref="PreconditionFailedException">
    /// Thrown when the saga row was advanced or removed by another writer since the caller loaded it.
    /// </exception>
    public Task SaveAsync<TState>(SagaInstance<TState> instance, CancellationToken cancellationToken = default)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));

        // The window-vs-own-scope split lives entirely in StageAsync. This save stages the write and
        // returns the token the row will carry; StageAsync applies it — deferred through the window's
        // commit, or stamped after its own guarded SaveChangesAsync — so a refused commit always leaves
        // instance.Version exactly as it was loaded. The own-scope commit is guarded so it surfaces a
        // conflict the same way the window's commit does.
        return StageAsync(
            db => StageSaveAsync(db, instance, cancellationToken),
            db => SagaConcurrencyGuard.SaveChangesOrRefuseAsync(db, () => Refused(instance), cancellationToken),
            next => instance.Version = next,
            cancellationToken);
    }

    /// <summary>
    /// Runs a staging action against the context of an open persistence window, leaving the commit to
    /// that window, or against a context of its own, which it then commits. This is the single place the
    /// window-vs-own-scope branch lives, so both <see cref="SaveAsync"/> and <see cref="DeleteAsync"/>
    /// reuse it instead of each duplicating the split. The value the stage produces — a saga's next
    /// concurrency token — is applied only once the write is durable: deferred through the window's
    /// <see cref="ISagaDbContextAccessor.OnCommitted"/> hook, or stamped after the own-scope commit.
    /// </summary>
    /// <typeparam name="T">The value the stage produces for the committing caller to apply.</typeparam>
    /// <param name="stage">Records the change on the change tracker and returns the value to apply on commit.</param>
    /// <param name="commit">Commits an own-scope context; unused on the windowed path, which the window commits.</param>
    /// <param name="apply">Applies the staged value once the write is durable, e.g. stamps the new token.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    private async Task StageAsync<T>(
        Func<ISagaDbContext, Task<T>> stage,
        Func<ISagaDbContext, Task> commit,
        Action<T> apply,
        CancellationToken cancellationToken)
    {
        // A window commits on behalf of the store, so the write is only staged here; the window's own
        // guarded commit surfaces a conflict the same way the own-scope path below does. The apply is
        // deferred to that commit, so a refused commit leaves the caller untouched.
        if (_window?.Current is { } staged)
        {
            var deferred = await stage(staged).ConfigureAwait(false);
            _window.OnCommitted(() => apply(deferred));
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISagaDbContext>();

        var next = await stage(db).ConfigureAwait(false);

        await commit(db).ConfigureAwait(false);

        // The write is durable: only now apply, so a refusal in commit above leaves the caller's
        // load-time state untouched.
        apply(next);
    }

    /// <summary>
    /// Stages the write of <paramref name="instance"/> onto <paramref name="db"/> and returns the new
    /// concurrency token the row will carry once the change is committed. The token is set on the
    /// tracked entity here — EF Core needs it to write — but never on the caller's instance: that stamp
    /// is deferred to the committing caller, so a refusal or a failed commit leaves the caller's
    /// instance carrying exactly the token it was loaded with.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="db">The context the change is staged onto.</param>
    /// <param name="instance">The saga instance to persist.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The token to stamp onto the caller's instance once the write is durable.</returns>
    /// <exception cref="PreconditionFailedException">Thrown when the write would overwrite or resurrect
    /// a row the caller is not entitled to write over.</exception>
    private async Task<Guid> StageSaveAsync<TState>(ISagaDbContext db, SagaInstance<TState> instance, CancellationToken cancellationToken)
    {
        var correlationKey = instance.CorrelationKey ?? string.Empty;

        var entity = await db.SagaStates
            .FirstOrDefaultAsync(e => e.SagaType == instance.SagaType && e.CorrelationKey == correlationKey, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            // The caller loaded a row that is now gone: a concurrent writer completed and removed the
            // saga. Resurrecting it would undo that completion, so the stale write is refused. An
            // instance that was never persisted (its Version is the unset sentinel) is a genuine
            // insert instead.
            if (instance.Version != Guid.Empty)
                throw Refused(instance);

            entity = new SagaStateEntity
            {
                SagaType = instance.SagaType,
                CorrelationKey = correlationKey,
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
            };
            db.SagaStates.Add(entity);
        }
        else if (instance.Version == Guid.Empty)
        {
            // A fresh, never-persisted instance found an existing row. Silently updating it here would
            // clobber that row with a plain UPDATE the unique index never catches: the cross-process
            // duplicate-trigger case, where two triggers both pass the start check and the later one's
            // read lands after the earlier one has committed. A genuine insert may only create a row
            // that is not there yet, so a fresh instance over an existing row is refused. The engine
            // reloads and re-handles the event against the saga that already exists.
            throw Refused(instance);
        }
        else if (entity.ConcurrencyToken != instance.Version)
        {
            // The row was advanced by another writer since the caller loaded it. Comparing the
            // load-time token before writing closes the wide window: a well-formed write built on a
            // stale snapshot is refused here rather than clobbering the other writer's step. The token
            // rolled forward below, checked by EF Core in the WHERE clause, closes the narrow window
            // — a writer whose read-and-save interval genuinely overlaps — on top of it.
            throw Refused(instance);
        }

        entity.StateTypeName = typeof(TState).AssemblyQualifiedName ?? typeof(TState).FullName ?? typeof(TState).Name;
        entity.StateJson = JsonSerializer.Serialize(instance.State);
        entity.CurrentStepIndex = instance.CurrentStepIndex;
        entity.IsComplete = instance.IsComplete;
        entity.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

        // Roll the concurrency token forward on the tracked entity. For an update, EF Core compares the
        // previously loaded value in the WHERE clause, so a writer whose snapshot is stale fails with a
        // DbUpdateConcurrencyException rather than silently clobbering the other writer's changes. The
        // caller stamps this onto its own instance only after the commit lands.
        var next = Guid.NewGuid();
        entity.ConcurrencyToken = next;
        return next;
    }

    /// <summary>Builds the refusal both concurrency checks raise, so they read the same to a caller.</summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="instance">The saga instance whose stale write is refused.</param>
    /// <returns>The exception to throw.</returns>
    private static PreconditionFailedException Refused<TState>(SagaInstance<TState> instance) =>
        new($"The saga '{instance.SagaType}' for correlation key '{instance.CorrelationKey}' was changed by another writer while this step was being prepared. Reload it and re-handle the event.");

    /// <inheritdoc />
    public async Task DeleteAsync(string sagaType, string correlationKey, CancellationToken cancellationToken = default)
    {
        if (sagaType is null) throw new ArgumentNullException(nameof(sagaType));
        if (correlationKey is null) throw new ArgumentNullException(nameof(correlationKey));

        // A remove has no token to stamp, so it stages through the shared seam with a nothing value and
        // a no-op apply. The own-scope commit is a plain SaveChangesAsync — a no-op when the row was
        // already gone, which EF Core skips as an empty round trip.
        await StageAsync<object?>(
            async db =>
            {
                await StageRemoveAsync(db, sagaType, correlationKey, cancellationToken).ConfigureAwait(false);
                return null;
            },
            db => db.SaveChangesAsync(cancellationToken),
            static _ => { },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task StageRemoveAsync(
        ISagaDbContext db, string sagaType, string correlationKey, CancellationToken cancellationToken)
    {
        var entity = await db.SagaStates
            .FirstOrDefaultAsync(e => e.SagaType == sagaType && e.CorrelationKey == correlationKey, cancellationToken)
            .ConfigureAwait(false);

        // A saga that is already gone needs no delete: a racing instance removed it first.
        if (entity is not null)
            db.SagaStates.Remove(entity);
    }
}
