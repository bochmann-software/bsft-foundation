using Bsft.Core;
using Bsft.EventAggregator.Saga.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.EFCore.Tests;

public class EFCoreSagaStoreConcurrencyTests
{
    private static ServiceProvider BuildProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseSqlite(connection));
        services.AddScoped<ISagaDbContext>(sp => sp.GetRequiredService<TestSagaDbContext>());
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<TestSagaDbContext>().Database.EnsureCreated();

        return provider;
    }

    private static async Task<SagaStateEntity> ReadRowAsync(SqliteConnection connection, string correlationKey)
    {
        var options = new DbContextOptionsBuilder<TestSagaDbContext>().UseSqlite(connection).Options;
        await using var ctx = new TestSagaDbContext(options);
        return await ctx.SagaStates.AsNoTracking().FirstAsync(e => e.CorrelationKey == correlationKey);
    }

    [Fact]
    public async Task SaveAsync_WhenAConcurrentWriterAlreadyAdvancedTheRow_RefusesTheStaleWriteWithoutLosingState()
    {
        // The tracker scenario: two workers load the same state (step 0) across two processes and both
        // advance to step 1. The optimistic-concurrency token must refuse the second write with a
        // conflict exception rather than silently overwriting the first — no saga step's state mutation
        // may be lost, and the loser must be signalled, not swallowed.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = BuildProvider(connection);
        var store = new EFCoreSagaStore(provider.GetRequiredService<IServiceScopeFactory>());

        var seed = new SagaInstance<TestSagaState>("OrderSaga", "order-1", new TestSagaState { Paid = false })
        {
            CurrentStepIndex = 0
        };
        await store.SaveAsync(seed);

        var workerA = await store.FindAsync<TestSagaState>("OrderSaga", "order-1");
        var workerB = await store.FindAsync<TestSagaState>("OrderSaga", "order-1");
        workerA!.Version.Should().Be(workerB!.Version);

        // Worker A wins the race: it advances the saga to step 1 and marks the order paid.
        workerA.CurrentStepIndex = 1;
        workerA.State.Paid = true;
        await store.SaveAsync(workerA);

        // Worker B advances its stale snapshot and saves. The store must refuse it instead of rolling
        // the row back over worker A's committed advance.
        workerB.CurrentStepIndex = 1;
        var act = async () => await store.SaveAsync(workerB);

        await act.Should().ThrowAsync<PreconditionFailedException>();

        var row = await ReadRowAsync(connection, "order-1");
        row.CurrentStepIndex.Should().Be(1, "worker A's advance survives");
        row.StateJson.Should().Contain("true", "worker A's state mutation is not lost to worker B's stale write");
    }

    [Fact]
    public async Task SaveAsync_WhenTwoAlternativesShareAStepIndex_TheLoserIsRefusedNotSilentlyDropped()
    {
        // The Or<TEvent> case: two structurally different transitions share one logical step index. A
        // PaymentApproved and a PaymentRejected both target step 1 with different state payloads. When
        // they race, the loser must surface a conflict — not silently overwrite the winner's state and
        // not vanish. The proof asserts the STATE payload, because the step index alone is identical
        // for both alternatives and would hide the loss.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = BuildProvider(connection);
        var store = new EFCoreSagaStore(provider.GetRequiredService<IServiceScopeFactory>());

        var seed = new SagaInstance<TestSagaState>("OrderSaga", "order-or", new TestSagaState { Paid = false })
        {
            CurrentStepIndex = 0
        };
        await store.SaveAsync(seed);

        var approved = await store.FindAsync<TestSagaState>("OrderSaga", "order-or");
        var rejected = await store.FindAsync<TestSagaState>("OrderSaga", "order-or");
        approved!.Version.Should().Be(rejected!.Version, "both alternatives loaded the same snapshot");

        // The approval alternative wins: it advances to step 1 and records a paid order.
        approved.CurrentStepIndex = 1;
        approved.State.Paid = true;
        await store.SaveAsync(approved);

        // The rejection alternative advanced to the very same step index with the opposite payload. It
        // must not silently overwrite the approval nor vanish — it is refused.
        rejected.CurrentStepIndex = 1;
        rejected.State.Paid = false;
        var act = async () => await store.SaveAsync(rejected);

        await act.Should().ThrowAsync<PreconditionFailedException>(
            "the losing alternative is refused, not silently dropped");

        var row = await ReadRowAsync(connection, "order-or");
        row.CurrentStepIndex.Should().Be(1);
        row.StateJson.Should().Contain("true",
            "the winning alternative's payload stands; the loser did not silently overwrite it");
    }

    [Fact]
    public async Task SaveAsync_OnRefusal_DoesNotMutateTheCallersInstance()
    {
        // The refuse-and-throw contract leaves the caller's instance untouched: the engine reloads
        // fresh state on the retry, so the store must not reconcile the loser's snapshot in place.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = BuildProvider(connection);
        var store = new EFCoreSagaStore(provider.GetRequiredService<IServiceScopeFactory>());

        var seed = new SagaInstance<TestSagaState>("OrderSaga", "order-keep", new TestSagaState()) { CurrentStepIndex = 0 };
        await store.SaveAsync(seed);

        var winner = await store.FindAsync<TestSagaState>("OrderSaga", "order-keep");
        var loser = await store.FindAsync<TestSagaState>("OrderSaga", "order-keep");

        winner!.CurrentStepIndex = 1;
        await store.SaveAsync(winner);

        var loserState = loser!.State;
        var loserVersion = loser.Version;
        loser.CurrentStepIndex = 5;

        var act = async () => await store.SaveAsync(loser);
        await act.Should().ThrowAsync<PreconditionFailedException>();

        loser.CurrentStepIndex.Should().Be(5, "the store does not roll the caller's index to the stored one");
        loser.State.Should().BeSameAs(loserState, "the store does not adopt the stored state into the caller's instance");
        loser.Version.Should().Be(loserVersion, "the store does not roll the caller's version forward on a refused write");
    }

    [Fact]
    public async Task SaveAsync_WhenTheRowWasRemovedByAConcurrentCompletion_RefusesInsteadOfResurrecting()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = BuildProvider(connection);
        var store = new EFCoreSagaStore(provider.GetRequiredService<IServiceScopeFactory>());

        var seed = new SagaInstance<TestSagaState>("OrderSaga", "order-5", new TestSagaState()) { CurrentStepIndex = 1 };
        await store.SaveAsync(seed);

        var stale = await store.FindAsync<TestSagaState>("OrderSaga", "order-5");

        // A concurrent process completed and removed the saga.
        await store.DeleteAsync("OrderSaga", "order-5");

        // The stale worker's save must refuse rather than recreate the completed saga.
        stale!.CurrentStepIndex = 2;
        var act = async () => await store.SaveAsync(stale);

        await act.Should().ThrowAsync<PreconditionFailedException>();

        var loaded = await store.FindAsync<TestSagaState>("OrderSaga", "order-5");
        loaded.Should().BeNull("a completed-and-removed saga is not resurrected by a stale write");
    }

    [Fact]
    public async Task SaveAsync_WhenAFreshInstanceTargetsAnExistingRow_RefusesInsteadOfOverwriting()
    {
        // The cross-process duplicate-trigger case: two triggers both pass their GetAsync start check,
        // and the loser's write lands after the winner has committed. The loser is a fresh, never-loaded
        // instance (its Version is the unset sentinel), so a plain UPDATE would clobber the committed
        // saga and the unique index would never fire. The store must refuse it, not silently adopt the
        // existing row.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = BuildProvider(connection);
        var store = new EFCoreSagaStore(provider.GetRequiredService<IServiceScopeFactory>());

        // The winning trigger inserted the saga and its follow-up events have advanced it.
        var committed = new SagaInstance<TestSagaState>("OrderSaga", "order-dup", new TestSagaState { Paid = true })
        {
            CurrentStepIndex = 3
        };
        await store.SaveAsync(committed);

        var duplicate = new SagaInstance<TestSagaState>("OrderSaga", "order-dup", new TestSagaState { Paid = false })
        {
            CurrentStepIndex = 1
        };
        duplicate.Version.Should().Be(Guid.Empty, "a duplicate trigger is a fresh, never-loaded instance");

        var act = async () => await store.SaveAsync(duplicate);
        await act.Should().ThrowAsync<PreconditionFailedException>();

        var row = await ReadRowAsync(connection, "order-dup");
        row.CurrentStepIndex.Should().Be(3, "the committed saga's advance survives the refused duplicate");
        row.StateJson.Should().Contain("true", "the duplicate's fresh state did not overwrite the committed saga");
        duplicate.Version.Should().Be(Guid.Empty, "a refusal does not stamp the fresh instance");
    }

    [Fact]
    public async Task SaveAsync_WhenANarrowWindowRefusalHappensAtCommit_LeavesTheInstanceVersionUnchanged()
    {
        // The narrow window the stored token closes at SaveChangesAsync: the pre-save guard passes (the
        // instance's token matches its own stale tracked row) so the store stages the new token onto the
        // tracked entity, yet the commit is refused. The caller's instance must still carry its
        // load-time token, never the never-persisted staged one.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = BuildProvider(connection);
        var store = new EFCoreSagaStore(provider.GetRequiredService<IServiceScopeFactory>());

        var seed = new SagaInstance<TestSagaState>("OrderSaga", "order-narrow-ver", new TestSagaState()) { CurrentStepIndex = 0 };
        await store.SaveAsync(seed);

        var options = new DbContextOptionsBuilder<TestSagaDbContext>().UseSqlite(connection).Options;
        await using var tracking = new TestSagaDbContext(options);
        var loaded = await tracking.SagaStates.FirstAsync(e => e.CorrelationKey == "order-narrow-ver");
        var loadedToken = loaded.ConcurrencyToken;

        // Somebody else moves the row on while the tracking context still holds what it loaded.
        var mover = await store.FindAsync<TestSagaState>("OrderSaga", "order-narrow-ver");
        mover!.CurrentStepIndex = 1;
        await store.SaveAsync(mover);

        var narrowStore = new EFCoreSagaStore(new FixedContextScopeFactory(tracking));
        var stale = new SagaInstance<TestSagaState>("OrderSaga", "order-narrow-ver", new TestSagaState { Paid = true })
        {
            CurrentStepIndex = 2,
            Version = loadedToken
        };

        var act = async () => await narrowStore.SaveAsync(stale);
        await act.Should().ThrowAsync<PreconditionFailedException>();

        stale.Version.Should().Be(loadedToken, "a narrow-window refusal at commit does not stamp the caller's instance");
    }

    [Fact]
    public async Task Version_WhenInstanceNeverPersisted_IsTheGuidEmptySentinel()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = BuildProvider(connection);
        var store = new EFCoreSagaStore(provider.GetRequiredService<IServiceScopeFactory>());

        var instance = new SagaInstance<TestSagaState>("OrderSaga", "order-new", new TestSagaState());
        instance.Version.Should().Be(Guid.Empty, "an unpersisted instance carries the unset sentinel, not null");

        // The unset sentinel means "never persisted", so the first save inserts instead of refusing.
        await store.SaveAsync(instance);
        instance.Version.Should().NotBe(Guid.Empty, "the first save stamps the row's concurrency token onto the instance");
    }

    [Fact]
    public async Task SaveAsync_StaleTrackedEntity_ThrowsDbUpdateConcurrencyException()
    {
        // A shared, open SQLite connection gives every context the same in-memory database while
        // honouring the optimistic-concurrency token (which the EF InMemory provider ignores).
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = BuildProvider(connection);
        var store = new EFCoreSagaStore(provider.GetRequiredService<IServiceScopeFactory>());

        var instance = new SagaInstance<TestSagaState>("OrderSaga", "order-2", new TestSagaState { Paid = false });
        await store.SaveAsync(instance);

        // A second worker reads the row (capturing its concurrency token) on its own tracked context.
        var staleOptions = new DbContextOptionsBuilder<TestSagaDbContext>().UseSqlite(connection).Options;
        await using var staleContext = new TestSagaDbContext(staleOptions);
        var staleEntity = await staleContext.SagaStates.FirstAsync(e => e.CorrelationKey == "order-2");

        // The first worker commits an update, advancing the concurrency token in the database.
        instance.State.Paid = true;
        await store.SaveAsync(instance);

        // The second worker now tries to persist its outdated tracked entity and must be rejected
        // instead of silently overwriting the first worker's change.
        staleEntity.CurrentStepIndex = 99;
        var act = async () => await staleContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task SaveAsync_AdvancesConcurrencyTokenOnEachUpdate()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = BuildProvider(connection);
        var store = new EFCoreSagaStore(provider.GetRequiredService<IServiceScopeFactory>());

        var instance = new SagaInstance<TestSagaState>("OrderSaga", "order-3", new TestSagaState());

        await store.SaveAsync(instance);
        var firstToken = (await ReadRowAsync(connection, "order-3")).ConcurrencyToken;
        instance.Version.Should().Be(firstToken, "the instance carries the token it was just written with");

        instance.CurrentStepIndex = 1;
        await store.SaveAsync(instance);
        var secondToken = (await ReadRowAsync(connection, "order-3")).ConcurrencyToken;
        secondToken.Should().NotBe(firstToken);
        instance.Version.Should().Be(secondToken);
    }

    [Fact]
    public async Task SaveAsync_WhenTheTrackedRowIsNoLongerTheStoredOne_IsRefusedAtCommit()
    {
        // The narrow window the stored token closes: a context that already tracks the row writes
        // against the values it loaded, so its pre-save guard passes (its token still matches its own
        // stale tracked row) yet the row it means to update is no longer the row that is there. The
        // conflict surfaces only at SaveChangesAsync, which the store translates into the same refusal.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = BuildProvider(connection);
        var store = new EFCoreSagaStore(provider.GetRequiredService<IServiceScopeFactory>());

        var seed = new SagaInstance<TestSagaState>("OrderSaga", "order-narrow", new TestSagaState()) { CurrentStepIndex = 0 };
        await store.SaveAsync(seed);

        // A context that already tracks the row as it was, holding its load-time token.
        var options = new DbContextOptionsBuilder<TestSagaDbContext>().UseSqlite(connection).Options;
        await using var tracking = new TestSagaDbContext(options);
        var loaded = await tracking.SagaStates.FirstAsync(e => e.CorrelationKey == "order-narrow");
        var loadedToken = loaded.ConcurrencyToken;

        // Somebody else moves the row on while the tracking context still holds what it loaded.
        var mover = await store.FindAsync<TestSagaState>("OrderSaga", "order-narrow");
        mover!.CurrentStepIndex = 1;
        await store.SaveAsync(mover);

        // A store whose scope hands out the stale tracking context. The instance carries the same
        // load-time token the tracked row holds, so the pre-save guard passes and the conflict only
        // surfaces at SaveChangesAsync — the narrow window the EF token closes on the self-committing path.
        var narrowStore = new EFCoreSagaStore(new FixedContextScopeFactory(tracking));
        var stale = new SagaInstance<TestSagaState>("OrderSaga", "order-narrow", new TestSagaState { Paid = true })
        {
            CurrentStepIndex = 2,
            Version = loadedToken
        };

        var act = async () => await narrowStore.SaveAsync(stale);

        await act.Should().ThrowAsync<PreconditionFailedException>();
    }

    [Fact]
    public void SagaStateEntityConfiguration_ConfiguresConcurrencyToken()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TestSagaDbContext>().UseSqlite(connection).Options;
        using var ctx = new TestSagaDbContext(options);

        var property = ctx.Model
            .FindEntityType(typeof(SagaStateEntity))!
            .FindProperty(nameof(SagaStateEntity.ConcurrencyToken))!;

        property.IsConcurrencyToken.Should().BeTrue("the saga state row is guarded by an optimistic-concurrency token");
    }

    /// <summary>Hands out one fixed <see cref="ISagaDbContext"/>, so a save runs against a context a
    /// test prepared — here one that already tracks the row and holds a now-stale token.</summary>
    private sealed class FixedContextScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly ISagaDbContext _context;

        public FixedContextScopeFactory(ISagaDbContext context) => _context = context;

        public IServiceProvider ServiceProvider => this;

        public IServiceScope CreateScope() => this;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(ISagaDbContext) ? _context : null;

        // The tracking context is owned by the test's await using, so the scope must not dispose it.
        public void Dispose()
        {
        }
    }
}
