using Bsft.EventAggregator.Saga.EFCore.Tests.Support;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.EFCore.Tests;

/// <summary>
/// Timeout persistence against SQLite over a shared open connection: the EF InMemory provider
/// ignores the concurrency token, which is exactly the mechanism under test here.
/// </summary>
public class EFCoreSagaTimeoutStoreTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly EFCoreSagaTimeoutStore _store;

    public EFCoreSagaTimeoutStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseSqlite(_connection));
        services.AddScoped<ISagaTimeoutDbContext>(sp => sp.GetRequiredService<TestSagaDbContext>());
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<TestSagaDbContext>().Database.EnsureCreated();

        _store = new EFCoreSagaTimeoutStore(_provider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public void Constructor_RejectsANullScopeFactory()
    {
        var act = () => new EFCoreSagaTimeoutStore(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
    }

    [Fact]
    public async Task ScheduleAsync_RejectsNull()
    {
        var act = async () => await _store.ScheduleAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("timeout");
    }

    [Fact]
    public async Task ATimeoutSurvivesAProcessRestart()
    {
        await _store.ScheduleAsync(Timeout("followup", _now.AddDays(2)));

        // A fresh store on the same database stands in for the restarted process.
        var afterRestart = new EFCoreSagaTimeoutStore(_provider.GetRequiredService<IServiceScopeFactory>());

        var due = await afterRestart.ListDueAsync(_now.AddDays(3), 10);
        due.Should().ContainSingle().Which.Name.Should().Be("followup");
    }

    [Fact]
    public async Task ScheduleAsync_UnderAnExistingName_ReplacesThePendingRow()
    {
        await _store.ScheduleAsync(Timeout("followup", _now.AddDays(2)));
        await _store.ScheduleAsync(Timeout("followup", _now.AddDays(5)));

        var due = await _store.ListDueAsync(_now.AddDays(10), 10);
        due.Should().ContainSingle().Which.DueAt.Should().Be(_now.AddDays(5));
    }

    [Fact]
    public async Task TwoInstancesSchedulingTheSameNameLeaveOnlyOnePendingTimer()
    {
        var second = new EFCoreSagaTimeoutStore(_provider.GetRequiredService<IServiceScopeFactory>());

        await _store.ScheduleAsync(Timeout("followup", _now.AddDays(2)));
        await second.ScheduleAsync(Timeout("followup", _now.AddDays(2)));

        // A name is unique among the pending timers of a saga instance, so a duplicate schedule must
        // not produce a second reminder.
        (await _store.ListDueAsync(_now.AddDays(10), 10)).Should().ContainSingle();
    }

    [Fact]
    public async Task ANameBecomesReusableOnceItsTimerHasFired()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var due = (await _store.ListDueAsync(_now, 10))[0];
        await _store.TryDispatchAsync(due, _now, (_, _) => Task.CompletedTask);

        // The dispatched row keeps its name, so the uniqueness only covers pending timers.
        await _store.ScheduleAsync(Timeout("followup", _now.AddDays(1)));

        (await _store.ListDueAsync(_now.AddDays(10), 10)).Should().ContainSingle();
    }

    [Fact]
    public async Task GetDueAsync_ReturnsOnlyPendingTimeoutsThatAreDue()
    {
        await _store.ScheduleAsync(Timeout("soon", _now.AddMinutes(-1)));
        await _store.ScheduleAsync(Timeout("later", _now.AddDays(2)));

        var due = await _store.ListDueAsync(_now, 10);

        due.Should().ContainSingle().Which.Name.Should().Be("soon");
    }

    [Fact]
    public async Task GetDueAsync_RoundTripsEveryField()
    {
        await _store.ScheduleAsync(Timeout("followup", _now.AddMinutes(-1), correlationId: "corr-9"));

        var stored = (await _store.ListDueAsync(_now, 10))[0];

        stored.SagaType.Should().Be("Sales");
        stored.CorrelationKey.Should().Be("quote-1");
        stored.StepIndex.Should().Be(1);
        stored.PayloadJson.Should().Be("{}");
        stored.PayloadTypeName.Should().Be(typeof(object).AssemblyQualifiedName);
        stored.CorrelationId.Should().Be("corr-9");
        stored.DispatchedAt.Should().BeNull();
        stored.ConcurrencyToken.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CancelAsync_RemovesOnlyTheNamedTimeout()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        await _store.ScheduleAsync(Timeout("callback", _now));

        await _store.CancelAsync("Sales", "quote-1", "followup");

        (await _store.ListDueAsync(_now, 10)).Should().ContainSingle().Which.Name.Should().Be("callback");
    }

    [Fact]
    public async Task CancelAsync_OnAnEmptyStore_DoesNothing()
    {
        var act = async () => await _store.CancelAsync("Sales", "quote-1", "followup");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CancelForStepAsync_RemovesTheRemindersOfThatStep()
    {
        await _store.ScheduleAsync(Timeout("followup", _now, stepIndex: 1));
        await _store.ScheduleAsync(Timeout("signature", _now, stepIndex: 2));

        await _store.CancelForStepAsync("Sales", "quote-1", 1);

        (await _store.ListDueAsync(_now, 10)).Should().ContainSingle().Which.Name.Should().Be("signature");
    }

    [Fact]
    public async Task CancelAllAsync_ClearsTheSagaInstance()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        await _store.ScheduleAsync(Timeout("callback", _now));

        await _store.CancelAllAsync("Sales", "quote-1");

        (await _store.ListDueAsync(_now, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task TryDispatchAsync_MarksTheTimeoutDispatched()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var due = (await _store.ListDueAsync(_now, 10))[0];

        var result = await _store.TryDispatchAsync(due, _now, (_, _) => Task.CompletedTask);

        result.Should().BeTrue();
        (await _store.ListDueAsync(_now, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task TryDispatchAsync_ValidatesItsArguments()
    {
        await FluentActions.Awaiting(() => _store.TryDispatchAsync(null!, _now, (_, _) => Task.CompletedTask))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("timeout");
        await FluentActions.Awaiting(() => _store.TryDispatchAsync(Timeout("x", _now), _now, null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("dispatch");
    }

    [Fact]
    public async Task TryDispatchAsync_ReturnsFalseForAnUnknownTimeoutWithoutPublishing()
    {
        var unknown = Timeout("gone", _now);
        unknown.Id = 999;

        var published = false;
        var result = await _store.TryDispatchAsync(unknown, _now, (_, _) =>
        {
            published = true;
            return Task.CompletedTask;
        });

        result.Should().BeFalse();
        published.Should().BeFalse();
    }

    [Fact]
    public async Task ATimerCancelledAfterThePollIsNotPublished()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var due = (await _store.ListDueAsync(_now, 10))[0];

        // The business event arrived between the poll and the dispatch — the reminder must not fire.
        await _store.CancelAsync("Sales", "quote-1", "followup");

        var published = false;
        var result = await _store.TryDispatchAsync(due, _now, (_, _) =>
        {
            published = true;
            return Task.CompletedTask;
        });

        result.Should().BeFalse();
        published.Should().BeFalse("a canceled reminder must never reach the saga");
    }

    [Fact]
    public async Task TwoInstances_FireOneDueTimeoutExactlyOnce()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var due = (await _store.ListDueAsync(_now, 10))[0];

        var second = new EFCoreSagaTimeoutStore(_provider.GetRequiredService<IServiceScopeFactory>());
        var publishes = 0;

        // The second instance completes its whole dispatch while the first one is still publishing,
        // so the first one's claim finds the token already rolled forward.
        var firstWon = await _store.TryDispatchAsync(due, _now, async (_, _) =>
        {
            publishes++;
            await second.TryDispatchAsync(due, _now, (_, _) =>
            {
                publishes++;
                return Task.CompletedTask;
            });
        });

        firstWon.Should().BeFalse();
        publishes.Should().Be(2, "delivery is at-least-once, so both may publish");
        (await _store.ListDueAsync(_now, 10)).Should().BeEmpty("exactly one of them wins the claim");
    }

    [Fact]
    public async Task SkipAsync_TakesTheTimeoutOutOfRotation()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var id = (await _store.ListDueAsync(_now, 10))[0].Id;

        await _store.SkipAsync([id], _now);

        (await _store.ListDueAsync(_now, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task SkipAsync_RetiresAWholeBatchInOneStatement()
    {
        await _store.ScheduleAsync(Timeout("a", _now));
        await _store.ScheduleAsync(Timeout("b", _now));
        var ids = (await _store.ListDueAsync(_now, 10)).Select(t => t.Id).ToList();

        await _store.SkipAsync(ids, _now);

        (await _store.ListDueAsync(_now, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task SkipAsync_WithAnEmptyBatchTouchesNothing()
    {
        await _store.ScheduleAsync(Timeout("a", _now));

        await _store.SkipAsync([], _now);

        (await _store.ListDueAsync(_now, 10)).Should().ContainSingle();
    }

    [Fact]
    public async Task SkipAsync_RejectsANullBatch()
    {
        var act = async () => await _store.SkipAsync(null!, _now);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("ids");
    }

    [Fact]
    public async Task RecordFailedAttemptAsync_IncrementsTheCounter()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var id = (await _store.ListDueAsync(_now, 10))[0].Id;

        await _store.RecordFailedAttemptAsync(id);

        (await _store.ListDueAsync(_now, 10))[0].AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task DeadLetterAsync_RetiresTheRowWithItsReasonAndLeavesTheDueSet()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var id = (await _store.ListDueAsync(_now, 10))[0].Id;

        await _store.DeadLetterAsync(id, _now, "the payload type is gone");

        (await _store.ListDueAsync(_now.AddYears(1), 10)).Should().BeEmpty();

        using var scope = _provider.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<TestSagaDbContext>()
            .SagaTimeouts.AsNoTracking().SingleAsync(e => e.Id == id);

        row.DeadLetteredAt.Should().Be(_now.UtcDateTime);
        row.DeadLetterReason.Should().Be("the payload type is gone");
        row.AttemptCount.Should().Be(1);

        // Stamping DispatchedAt too is what lifts the row out of the filtered pending-name index, so
        // the saga can schedule that name again instead of colliding with a timer that never fired.
        row.DispatchedAt.Should().Be(_now.UtcDateTime);
    }

    [Fact]
    public async Task DeadLetterAsync_FreesThePendingNameForANewTimer()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var id = (await _store.ListDueAsync(_now, 10))[0].Id;
        await _store.DeadLetterAsync(id, _now, "the payload type is gone");

        await _store.ScheduleAsync(Timeout("followup", _now));

        (await _store.ListDueAsync(_now, 10)).Should().ContainSingle().Which.Id.Should().NotBe(id);
    }

    [Fact]
    public async Task DeadLetterAsync_LeavesADispatchedTimeoutAlone()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var due = (await _store.ListDueAsync(_now, 10))[0];
        await _store.TryDispatchAsync(due, _now, (_, _) => Task.CompletedTask);

        await _store.DeadLetterAsync(due.Id, _now.AddMinutes(1), "too late");

        using var scope = _provider.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<TestSagaDbContext>()
            .SagaTimeouts.AsNoTracking().SingleAsync(e => e.Id == due.Id);

        // A retirement racing a successful dispatch must not rewrite a fired timer into a failed one.
        row.DeadLetteredAt.Should().BeNull();
        row.DispatchedAt.Should().Be(_now.UtcDateTime);
    }

    [Fact]
    public async Task DeadLetterAsync_CutsAnOverlongReasonToTheColumnWidth()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var id = (await _store.ListDueAsync(_now, 10))[0].Id;

        // A surrogate pair straddling the cut would leave an unpaired surrogate behind.
        var reason = new string('x', SagaTimeoutEntityConfiguration.DeadLetterReasonMaxLength - 1) + "😀";

        await _store.DeadLetterAsync(id, _now, reason);

        using var scope = _provider.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<TestSagaDbContext>()
            .SagaTimeouts.AsNoTracking().SingleAsync(e => e.Id == id);

        row.DeadLetterReason.Should().HaveLength(SagaTimeoutEntityConfiguration.DeadLetterReasonMaxLength - 1);
        char.IsSurrogate(row.DeadLetterReason![^1]).Should().BeFalse();
    }

    [Fact]
    public async Task DeadLetterAsync_RejectsAMissingReason()
    {
        var act = async () => await _store.DeadLetterAsync(1, _now, null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("reason");
    }

    private static SagaTimeout Timeout(
        string name,
        DateTimeOffset dueAt,
        int stepIndex = 1,
        string? correlationId = null) => new()
        {
            SagaType = "Sales",
            CorrelationKey = "quote-1",
            Name = name,
            StepIndex = stepIndex,
            DueAt = dueAt,
            PayloadJson = "{}",
            PayloadTypeName = typeof(object).AssemblyQualifiedName!,
            CorrelationId = correlationId,
            ConcurrencyToken = Guid.NewGuid(),
        };

    // A null addresses no row, so an unguarded cancel would report success while the timer it was
    // meant to withdraw stays pending and fires later.

    [Theory]
    [InlineData(null, "quote-1", "reminder")]
    [InlineData("Sales", null, "reminder")]
    [InlineData("Sales", "quote-1", null)]
    public async Task CancelAsync_RejectsANullThatWouldAddressNoRow(string? sagaType, string? correlationKey, string? name)
    {
        var act = () => _store.CancelAsync(sagaType!, correlationKey!, name!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null, "quote-1")]
    [InlineData("Sales", null)]
    public async Task CancelForStepAsync_RejectsANullThatWouldAddressNoRow(string? sagaType, string? correlationKey)
    {
        var act = () => _store.CancelForStepAsync(sagaType!, correlationKey!, stepIndex: 0);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null, "quote-1")]
    [InlineData("Sales", null)]
    public async Task CancelAllAsync_RejectsANullThatWouldAddressNoRow(string? sagaType, string? correlationKey)
    {
        var act = () => _store.CancelAllAsync(sagaType!, correlationKey!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
