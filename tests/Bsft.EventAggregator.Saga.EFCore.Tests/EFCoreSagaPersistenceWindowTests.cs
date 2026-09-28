using Bsft.Core;
using Bsft.EventAggregator.Saga.EFCore.Tests.Support;
using Bsft.EventAggregator.Saga.Persistence;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.EFCore.Tests;

/// <summary>
/// The shared persist window: while it is open both saga stores stage into its context, and nothing
/// reaches the database until it is committed. SQLite over one open connection, because the EF
/// InMemory provider has no transaction to roll back.
/// </summary>
public class EFCoreSagaPersistenceWindowTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly EFCoreSagaPersistenceWindow _window;
    private readonly EFCoreSagaStore _states;
    private readonly IEFCoreSagaTimeoutStore _timeouts;

    public EFCoreSagaPersistenceWindowTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseSqlite(_connection));
        services.AddScoped<ISagaDbContext>(sp => sp.GetRequiredService<TestSagaDbContext>());
        services.AddScoped<ISagaTimeoutDbContext>(sp => sp.GetRequiredService<TestSagaDbContext>());
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<TestSagaDbContext>().Database.EnsureCreated();

        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        _window = new EFCoreSagaPersistenceWindow(scopeFactory);
        _states = new EFCoreSagaStore(scopeFactory, window: _window);
        _timeouts = new EFCoreSagaTimeoutStore(scopeFactory, _window);
    }

    [Fact]
    public void Constructor_RejectsANullScopeFactory()
    {
        var act = () => new EFCoreSagaPersistenceWindow(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
    }

    [Fact]
    public void WithoutAnOpenWindow_NoContextIsCurrent()
    {
        _window.Current.Should().BeNull();
    }

    [Fact]
    public async Task AnOpenWindow_PublishesItsContext_AndTakesItBackOnDispose()
    {
        await using (var scope = await _window.OpenAsync())
        {
            _window.Current.Should().NotBeNull();
            await scope.CommitAsync();
        }

        _window.Current.Should().BeNull();
    }

    [Fact]
    public async Task OpeningAWindowWithoutARegisteredContext_ThrowsAndDisposesTheScope()
    {
        var factory = new RecordingScopeFactory(new ServiceCollection().BuildServiceProvider());
        var window = new EFCoreSagaPersistenceWindow(factory);

        var act = async () => await window.OpenAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        factory.LastScopeDisposed.Should().BeTrue("a scope nobody received would leak its connection");
        window.Current.Should().BeNull();
    }

    [Fact]
    public async Task TheTimeoutStore_ReportsWhichModeItsNextWriteWouldTake()
    {
        _timeouts.IsStagingIntoWindow.Should().BeFalse();

        await using (var scope = await _window.OpenAsync())
        {
            _timeouts.IsStagingIntoWindow.Should().BeTrue();
            await scope.CommitAsync();
        }

        _timeouts.IsStagingIntoWindow.Should().BeFalse();
    }

    [Fact]
    public async Task AStoreWithoutAWindow_NeverStages()
    {
        IEFCoreSagaTimeoutStore solo = new EFCoreSagaTimeoutStore(_provider.GetRequiredService<IServiceScopeFactory>());

        await using (await _window.OpenAsync())
        {
            solo.IsStagingIntoWindow.Should().BeFalse("it was never given the accessor");
        }
    }

    [Fact]
    public async Task TheStateAdvanceAndTheTimerWrite_BecomeDurableTogether()
    {
        await using (var scope = await _window.OpenAsync())
        {
            await _states.SaveAsync(Instance(stepIndex: 2));
            await _timeouts.ScheduleAsync(Timeout("followup"));

            // Nothing has been committed yet, so a reader outside the window still sees an empty database.
            (await ReadStateAsync()).Should().BeNull();
            (await ReadTimeoutNamesAsync()).Should().BeEmpty();

            await scope.CommitAsync();
        }

        (await ReadStateAsync())!.CurrentStepIndex.Should().Be(2);
        (await ReadTimeoutNamesAsync()).Should().BeEquivalentTo(["followup"]);
    }

    [Fact]
    public async Task AWindowThatIsNeverCommitted_LeavesNothingBehind()
    {
        await using (await _window.OpenAsync())
        {
            await _states.SaveAsync(Instance(stepIndex: 2));
            await _timeouts.ScheduleAsync(Timeout("followup"));
        }

        (await ReadStateAsync()).Should().BeNull();
        (await ReadTimeoutNamesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AWindowAbandonedAfterTheStateAdvance_LeavesTheEarlierStateIntact()
    {
        // The saga reached step 1 in an earlier, committed window. The live instance carries the token
        // that commit stamped, exactly as the engine reuses it for the next step.
        var instance = Instance(stepIndex: 1);
        await using (var first = await _window.OpenAsync())
        {
            await _states.SaveAsync(instance);
            await first.CommitAsync();
        }

        await using (await _window.OpenAsync())
        {
            instance.CurrentStepIndex = 2;
            await _states.SaveAsync(instance);

            // The timer write fails here; the window is abandoned instead of committed.
        }

        (await ReadStateAsync())!.CurrentStepIndex.Should().Be(1, "the saga must not advance past reminders it never got");
    }

    [Fact]
    public async Task CancellingInsideAWindow_IsStagedRatherThanExecutedImmediately()
    {
        await _timeouts.ScheduleAsync(Timeout("followup"));

        await using (await _window.OpenAsync())
        {
            await _timeouts.CancelAllAsync("QuoteSaga", "q-1");

            // ExecuteDelete would already be gone from the database at this point.
            (await ReadTimeoutNamesAsync()).Should().BeEquivalentTo(["followup"]);
        }

        (await ReadTimeoutNamesAsync()).Should().BeEquivalentTo(["followup"], "the window was never committed");
    }

    [Fact]
    public async Task SchedulingAndCancellingTheSameTimerInOneWindow_LeavesNothingPending()
    {
        await using (var scope = await _window.OpenAsync())
        {
            await _timeouts.ScheduleAsync(Timeout("followup"));
            await _timeouts.CancelAsync("QuoteSaga", "q-1", "followup");

            await scope.CommitAsync();
        }

        // The cancel cannot see the schedule in the database — it has not been committed yet — so a
        // cancellation that only queries would let the reminder through.
        (await ReadTimeoutNamesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ReschedulingTheSameNameInOneWindow_LeavesOnlyTheLastReminder()
    {
        await using (var scope = await _window.OpenAsync())
        {
            await _timeouts.ScheduleAsync(Timeout("followup", _now.AddDays(2)));
            await _timeouts.ScheduleAsync(Timeout("followup", _now.AddDays(5)));

            await scope.CommitAsync();
        }

        // The second schedule cannot see the first in the database — it has not been committed yet —
        // so a deduplication that only queries stages two inserts, and the commit dies on the unique
        // pending-name index instead of the reschedule replacing the reminder.
        var pending = await ReadPendingTimeoutsAsync();
        pending.Should().ContainSingle().Which.DueAt.Should().Be(_now.AddDays(5).UtcDateTime);
    }

    [Fact]
    public async Task ACommittedCancellation_RemovesThePendingTimer()
    {
        await _timeouts.ScheduleAsync(Timeout("followup"));

        await using (var scope = await _window.OpenAsync())
        {
            await _timeouts.CancelAllAsync("QuoteSaga", "q-1");
            await scope.CommitAsync();
        }

        (await ReadTimeoutNamesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RemovingACompletedSagaInsideAWindow_IsStagedToo()
    {
        await _states.SaveAsync(Instance(stepIndex: 1));

        await using (var scope = await _window.OpenAsync())
        {
            await _states.DeleteAsync("QuoteSaga", "q-1");
            (await ReadStateAsync()).Should().NotBeNull();

            await scope.CommitAsync();
        }

        (await ReadStateAsync()).Should().BeNull();
    }

    [Fact]
    public async Task RemovingASagaThatIsNotThere_SavesNothing()
    {
        // A completing step whose state was already removed — by a racing instance, or because the
        // saga never persisted — must not turn into a failed step.
        var act = async () => await _states.DeleteAsync("QuoteSaga", "never-existed");

        await act.Should().NotThrowAsync();
        (await ReadStateAsync()).Should().BeNull();
    }

    [Fact]
    public async Task OutsideAWindow_BothStoresStillCommitOnTheirOwn()
    {
        await _states.SaveAsync(Instance(stepIndex: 1));
        await _timeouts.ScheduleAsync(Timeout("followup"));

        (await ReadStateAsync())!.CurrentStepIndex.Should().Be(1);
        (await ReadTimeoutNamesAsync()).Should().BeEquivalentTo(["followup"]);
    }

    [Fact]
    public async Task AConflictingCommit_SurfacesAPreconditionFailure_AndSchedulesNoTimeout()
    {
        // The saga reached step 1 in an earlier committed window; its live instance carries that token.
        var instance = Instance(stepIndex: 1);
        await _states.SaveAsync(instance);

        await using var scope = await _window.OpenAsync();

        // Stage the next step advance and a follow-up reminder into the one window commit.
        instance.CurrentStepIndex = 2;
        await _states.SaveAsync(instance);
        await _timeouts.ScheduleAsync(Timeout("followup"));

        // Between the window's read and its commit, another writer advances the very same row, bumping
        // its concurrency token so the window's staged WHERE clause no longer matches.
        await AdvanceRowOutOfBandAsync();

        var commit = async () => await scope.CommitAsync();

        await commit.Should().ThrowAsync<PreconditionFailedException>(
            "the windowed commit surfaces the conflict as the framework's exception, not a raw DbUpdateConcurrencyException");

        // The state advance and the reminder shared the refused commit, so neither landed: the row keeps
        // the out-of-band writer's value and no timeout is scheduled for a saga whose save was refused.
        (await ReadStateAsync())!.CurrentStepIndex.Should().Be(7, "the window's refused advance did not land");
        (await ReadTimeoutNamesAsync()).Should().BeEmpty("no reminder is scheduled for a refused save");
    }

    [Fact]
    public async Task SaveAsync_WhenTheWindowCommits_StampsTheInstanceVersionOnlyAfterTheCommit()
    {
        var instance = Instance(stepIndex: 2);
        instance.Version.Should().Be(Guid.Empty, "a fresh instance carries the unset sentinel");

        await using (var scope = await _window.OpenAsync())
        {
            await _states.SaveAsync(instance);

            // The window has not committed yet, so the caller's instance must not carry a token the store
            // only staged: a refused commit would otherwise leave it with a never-persisted version.
            instance.Version.Should().Be(Guid.Empty, "the version stamp is deferred until the commit lands");

            await scope.CommitAsync();
        }

        instance.Version.Should().NotBe(Guid.Empty, "the commit landed, so the staged token is now stamped");
        (await ReadStateAsync())!.ConcurrencyToken.Should().Be(instance.Version, "the stamped token matches the committed row");
    }

    [Fact]
    public async Task SaveAsync_WhenTheWindowedCommitIsRefused_LeavesTheInstanceVersionUnchanged()
    {
        // The saga reached step 1 in an earlier committed window; its live instance carries that token.
        var instance = Instance(stepIndex: 1);
        await _states.SaveAsync(instance);
        var committedToken = instance.Version;

        await using var scope = await _window.OpenAsync();
        instance.CurrentStepIndex = 2;
        await _states.SaveAsync(instance);

        // Another writer advances the same row before the window commits, so the staged WHERE clause no
        // longer matches and the commit is refused.
        await AdvanceRowOutOfBandAsync();

        var commit = async () => await scope.CommitAsync();
        await commit.Should().ThrowAsync<PreconditionFailedException>();

        instance.Version.Should().Be(committedToken, "a refused windowed commit does not stamp the caller's instance");
    }

    [Fact]
    public void OnCommitted_RejectsANullCallback()
    {
        var act = () => _window.OnCommitted(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("onCommitted");
    }

    [Fact]
    public void OnCommitted_OutsideAnyWindow_IsANoOp()
    {
        // No window is open, so there is nothing to stamp on commit: the registration is silently dropped
        // rather than throwing.
        var act = () => _window.OnCommitted(() => { });

        act.Should().NotThrow();
    }

    /// <summary>Advances the saga row on its own context, standing in for a concurrent writer.</summary>
    private async Task AdvanceRowOutOfBandAsync()
    {
        var options = new DbContextOptionsBuilder<TestSagaDbContext>().UseSqlite(_connection).Options;
        await using var ctx = new TestSagaDbContext(options);
        var row = await ctx.SagaStates.FirstAsync(e => e.CorrelationKey == "q-1");
        row.CurrentStepIndex = 7;
        row.ConcurrencyToken = Guid.NewGuid();
        await ctx.SaveChangesAsync();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static SagaInstance<QuoteState> Instance(int stepIndex) =>
        new("QuoteSaga", "q-1", new QuoteState()) { CurrentStepIndex = stepIndex };

    private static SagaTimeout Timeout(string name, DateTimeOffset? dueAt = null) => new()
    {
        SagaType = "QuoteSaga",
        CorrelationKey = "q-1",
        Name = name,
        StepIndex = 1,
        DueAt = dueAt ?? _now.AddDays(2),
        PayloadJson = "{}",
        PayloadTypeName = typeof(QuoteState).AssemblyQualifiedName!,
        ConcurrencyToken = Guid.NewGuid(),
    };

    /// <summary>Reads through a context of its own, so only committed rows are visible.</summary>
    private async Task<SagaStateEntity?> ReadStateAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TestSagaDbContext>()
            .SagaStates.AsNoTracking().FirstOrDefaultAsync();
    }

    /// <summary>Reads the committed pending rows, so a staged insert that never landed is invisible.</summary>
    private async Task<IReadOnlyList<SagaTimeoutEntity>> ReadPendingTimeoutsAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TestSagaDbContext>()
            .SagaTimeouts.AsNoTracking().Where(e => e.DispatchedAt == null).ToListAsync();
    }

    private async Task<IReadOnlyList<string>> ReadTimeoutNamesAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TestSagaDbContext>()
            .SagaTimeouts.AsNoTracking().Where(e => e.DispatchedAt == null).Select(e => e.Name).ToListAsync();
    }

    public class QuoteState
    {
        public bool Accepted { get; set; }
    }

    /// <summary>Hands out scopes and remembers whether the last one was disposed.</summary>
    private sealed class RecordingScopeFactory : IServiceScopeFactory
    {
        private readonly ServiceProvider _provider;

        public RecordingScopeFactory(ServiceProvider provider) => _provider = provider;

        public bool LastScopeDisposed { get; private set; }

        public IServiceScope CreateScope() => new RecordingScope(this, _provider.CreateScope());

        private sealed class RecordingScope : IServiceScope
        {
            private readonly RecordingScopeFactory _owner;
            private readonly IServiceScope _inner;

            public RecordingScope(RecordingScopeFactory owner, IServiceScope inner)
            {
                _owner = owner;
                _inner = inner;
                owner.LastScopeDisposed = false;
            }

            public IServiceProvider ServiceProvider => _inner.ServiceProvider;

            public void Dispose()
            {
                _owner.LastScopeDisposed = true;
                _inner.Dispose();
            }
        }
    }
}
