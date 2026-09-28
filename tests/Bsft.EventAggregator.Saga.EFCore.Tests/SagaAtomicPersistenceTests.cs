using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.EFCore.DependencyInjection;
using Bsft.EventAggregator.Saga.EFCore.Tests.Support;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.EFCore.Tests;

/// <summary>
/// The defect this closes, seen from the outside: a step advances the saga and schedules its
/// reminders, and a timer write that fails must not leave the saga standing past reminders that were
/// never written.
/// </summary>
public class SagaAtomicPersistenceTests : IDisposable
{
    private const string SAGA_TYPE = "QuoteState_QuoteSent";

    private readonly SqliteConnection _connection;

    public SagaAtomicPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public record QuoteSent(string QuoteId);
    public record QuoteAccepted(string QuoteId);
    public record QuoteFollowUpDue(string QuoteId);

    public class QuoteState
    {
        public bool Accepted { get; set; }
    }

    /// <summary>Advances to a waiting step and schedules a reminder while doing so.</summary>
    public class SalesFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<QuoteState>()
                .StartWith<QuoteSent>(e => e.QuoteId)
                .Then((_, _) => Task.CompletedTask)
                .WaitFor<QuoteAccepted>(e => e.QuoteId)
                .Then((ctx, e) =>
                {
                    ctx.State.Accepted = true;
                    ctx.ScheduleTimeout("followup", TimeSpan.FromDays(2), new QuoteFollowUpDue(e.QuoteId));
                    return Task.CompletedTask;
                })
                .WaitFor<QuoteFollowUpDue>(e => e.QuoteId)
                .Then((_, _) => Task.CompletedTask)
                .Complete();
        }
    }

    [Fact]
    public async Task AStepThatAdvancesAndSchedules_CommitsBothEffects()
    {
        using var sp = BuildProvider(failTimerWrites: false);
        var events = sp.GetRequiredService<IEventAggregator>();

        await events.PublishAsync(new QuoteSent("q-1"));
        await events.PublishAsync(new QuoteAccepted("q-1"));

        var stored = await sp.GetRequiredService<ISagaStore>().FindAsync<QuoteState>(SAGA_TYPE, "q-1");
        stored!.CurrentStepIndex.Should().Be(2);
        stored.State.Accepted.Should().BeTrue();
        (await PendingNamesAsync(sp)).Should().BeEquivalentTo(["followup"]);
    }

    [Fact]
    public async Task AFailingTimerWrite_LeavesTheSagaStateUnchanged()
    {
        using var sp = BuildProvider(failTimerWrites: true);
        var events = sp.GetRequiredService<IEventAggregator>();
        await events.PublishAsync(new QuoteSent("q-1"));

        var act = async () => await events.PublishAsync(new QuoteAccepted("q-1"));

        (await act.Should().ThrowAsync<AggregateException>())
            .Which.Flatten().InnerExceptions.Should().ContainSingle(e => e.Message.Contains("timer store is down"));

        var stored = await sp.GetRequiredService<ISagaStore>().FindAsync<QuoteState>(SAGA_TYPE, "q-1");
        stored!.CurrentStepIndex.Should().Be(1, "the state advance shares the transaction with the timer write");
        stored.State.Accepted.Should().BeFalse();
        (await PendingNamesAsync(sp)).Should().BeEmpty();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static async Task<IReadOnlyList<string>> PendingNamesAsync(IServiceProvider sp)
    {
        var pending = await sp.GetRequiredService<ISagaTimeoutStore>().ListDueAsync(DateTimeOffset.MaxValue, 100);
        return pending.Select(t => t.Name).ToList();
    }

    private ServiceProvider BuildProvider(bool failTimerWrites)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseSqlite(_connection));
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        services.AddBsftSagaEFCoreWithTimeouts<TestSagaDbContext>();
        services.AddBsftSagaFlow<SalesFlow>();

        if (failTimerWrites)
        {
            // Stands in for the database rejecting the insert: the write fails after the state
            // advance has been staged, which is precisely the window the fix has to close.
            services.AddSingleton<ISagaTimeoutStore>(sp => new FailingScheduleStore(
                new EFCoreSagaTimeoutStore(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<ISagaDbContextAccessor>())));
        }

        var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
            scope.ServiceProvider.GetRequiredService<TestSagaDbContext>().Database.EnsureCreated();

        sp.UseBsftSagaFlows();
        return sp;
    }

    /// <summary>Delegates everything except the one write that is made to fail.</summary>
    private sealed class FailingScheduleStore : ISagaTimeoutStore
    {
        private readonly ISagaTimeoutStore _inner;

        public FailingScheduleStore(ISagaTimeoutStore inner) => _inner = inner;

        public Task ScheduleAsync(SagaTimeout timeout, CancellationToken ct = default) =>
            throw new InvalidOperationException("the timer store is down");

        public Task CancelAsync(string sagaType, string correlationKey, string name, CancellationToken ct = default) =>
            _inner.CancelAsync(sagaType, correlationKey, name, ct);

        public Task CancelForStepAsync(string sagaType, string correlationKey, int stepIndex, CancellationToken ct = default) =>
            _inner.CancelForStepAsync(sagaType, correlationKey, stepIndex, ct);

        public Task CancelAllAsync(string sagaType, string correlationKey, CancellationToken ct = default) =>
            _inner.CancelAllAsync(sagaType, correlationKey, ct);

        public Task<IReadOnlyList<SagaTimeout>> ListDueAsync(DateTimeOffset now, int batchSize, CancellationToken ct = default) =>
            _inner.ListDueAsync(now, batchSize, ct);

        public Task<bool> TryDispatchAsync(
            SagaTimeout timeout,
            DateTimeOffset dispatchedAt,
            Func<SagaTimeout, CancellationToken, Task> dispatch,
            CancellationToken ct = default) =>
            _inner.TryDispatchAsync(timeout, dispatchedAt, dispatch, ct);

        public Task SkipAsync(IReadOnlyCollection<long> ids, DateTimeOffset skippedAt, CancellationToken ct = default) =>
            _inner.SkipAsync(ids, skippedAt, ct);

        public Task RecordFailedAttemptAsync(long id, CancellationToken ct = default) =>
            _inner.RecordFailedAttemptAsync(id, ct);

        public Task DeadLetterAsync(long id, DateTimeOffset deadLetteredAt, string reason, CancellationToken ct = default) =>
            _inner.DeadLetterAsync(id, deadLetteredAt, reason, ct);
    }
}
