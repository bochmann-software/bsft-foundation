using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Timeouts;

public class InMemorySagaTimeoutStoreTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemorySagaTimeoutStore _store = new();

    [Fact]
    public async Task ScheduleAsync_AssignsAnIdAndMakesTheTimeoutDiscoverable()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));

        var due = await _store.ListDueAsync(_now, 10);

        due.Should().ContainSingle();
        due[0].Id.Should().BeGreaterThan(0);
        due[0].Name.Should().Be("followup");
    }

    [Fact]
    public async Task ScheduleAsync_RejectsNull()
    {
        var act = async () => await _store.ScheduleAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("timeout");
    }

    [Fact]
    public async Task ScheduleAsync_UnderAnExistingName_ReplacesThePendingTimeout()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        await _store.ScheduleAsync(Timeout("followup", _now.AddHours(1)));

        var due = await _store.ListDueAsync(_now.AddDays(1), 10);

        due.Should().ContainSingle().Which.DueAt.Should().Be(_now.AddHours(1));
    }

    [Fact]
    public async Task GetDueAsync_IgnoresTimeoutsThatAreNotDueYet()
    {
        await _store.ScheduleAsync(Timeout("later", _now.AddHours(2)));

        (await _store.ListDueAsync(_now, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetDueAsync_ReturnsOldestFirstAndHonoursTheBatchSize()
    {
        await _store.ScheduleAsync(Timeout("b", _now.AddMinutes(-1)));
        await _store.ScheduleAsync(Timeout("a", _now.AddMinutes(-5)));

        var due = await _store.ListDueAsync(_now, 1);

        due.Should().ContainSingle().Which.Name.Should().Be("a");
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
    public async Task CancelForStepAsync_RemovesTheRemindersOfThatStepOnly()
    {
        await _store.ScheduleAsync(Timeout("followup", _now, stepIndex: 1));
        await _store.ScheduleAsync(Timeout("signature", _now, stepIndex: 2));

        await _store.CancelForStepAsync("Sales", "quote-1", 1);

        (await _store.ListDueAsync(_now, 10)).Should().ContainSingle().Which.Name.Should().Be("signature");
    }

    [Fact]
    public async Task CancelAllAsync_ClearsTheWholeSagaInstance()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        await _store.ScheduleAsync(Timeout("callback", _now));

        await _store.CancelAllAsync("Sales", "quote-1");

        (await _store.ListDueAsync(_now, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task CancelAllAsync_LeavesOtherSagaInstancesAlone()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        await _store.ScheduleAsync(Timeout("followup", _now, correlationKey: "quote-2"));

        await _store.CancelAllAsync("Sales", "quote-1");

        (await _store.ListDueAsync(_now, 10)).Should().ContainSingle().Which.CorrelationKey.Should().Be("quote-2");
    }

    [Fact]
    public async Task TryDispatchAsync_RunsTheCallbackAndMarksTheTimeoutDispatched()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var due = (await _store.ListDueAsync(_now, 10))[0];

        var dispatched = 0;
        var result = await _store.TryDispatchAsync(due, _now, (_, _) =>
        {
            dispatched++;
            return Task.CompletedTask;
        });

        result.Should().BeTrue();
        dispatched.Should().Be(1);
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
    public async Task TryDispatchAsync_ReturnsFalseWhenTheTimeoutWasAlreadyDispatched()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var due = (await _store.ListDueAsync(_now, 10))[0];
        await _store.TryDispatchAsync(due, _now, (_, _) => Task.CompletedTask);

        (await _store.TryDispatchAsync(due, _now, (_, _) => Task.CompletedTask)).Should().BeFalse();
    }

    [Fact]
    public async Task ATimerCancelledWhileItIsBeingPublishedIsNotReportedAsDispatched()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var due = (await _store.ListDueAsync(_now, 10))[0];

        // The business event lands while the reminder is mid-publish; the claim must fail.
        var result = await _store.TryDispatchAsync(due, _now, async (_, _) =>
            await _store.CancelAsync("Sales", "quote-1", "followup"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryDispatchAsync_LosingTheClaimRaceAfterPublishing_ReportsFalse()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var due = (await _store.ListDueAsync(_now, 10))[0];

        // Simulate the other instance winning while this one is still publishing.
        var result = await _store.TryDispatchAsync(due, _now, async (_, _) =>
            await _store.TryDispatchAsync(due, _now, (_, _) => Task.CompletedTask));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SkipAsync_TakesTheTimeoutOutOfRotationWithoutDispatching()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var id = (await _store.ListDueAsync(_now, 10))[0].Id;

        await _store.SkipAsync([id], _now);

        (await _store.ListDueAsync(_now, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task SkipAsync_IgnoresAnUnknownTimeout()
    {
        var act = async () => await _store.SkipAsync([999L], _now);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SkipAsync_LeavesAnAlreadyDispatchedTimeoutAlone()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var due = (await _store.ListDueAsync(_now, 10))[0];
        await _store.TryDispatchAsync(due, _now, (_, _) => Task.CompletedTask);

        await _store.SkipAsync([due.Id], _now.AddHours(1));

        // The dispatch timestamp must survive: a skip racing a fire must not rewrite history.
        (await _store.ListDueAsync(_now.AddDays(1), 10)).Should().BeEmpty();
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
    public async Task RecordFailedAttemptAsync_IgnoresAnUnknownTimeout()
    {
        var act = async () => await _store.RecordFailedAttemptAsync(999);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeadLetterAsync_TakesTheTimeoutOutOfTheDueSetForGood()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var id = (await _store.ListDueAsync(_now, 10))[0].Id;

        await _store.DeadLetterAsync(id, _now, "the payload type is gone");

        (await _store.ListDueAsync(_now.AddYears(1), 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeadLetterAsync_RecordsTheReasonAndCountsTheAttemptThatRetiredIt()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var id = (await _store.ListDueAsync(_now, 10))[0].Id;

        await _store.DeadLetterAsync(id, _now, "the payload type is gone");

        // Retiring frees the name, so the saga can schedule that timer again.
        await _store.ScheduleAsync(Timeout("followup", _now));
        (await _store.ListDueAsync(_now, 10)).Should().ContainSingle().Which.Id.Should().NotBe(id);
    }

    [Fact]
    public async Task DeadLetterAsync_LeavesADispatchedTimeoutAlone()
    {
        await _store.ScheduleAsync(Timeout("followup", _now));
        var due = (await _store.ListDueAsync(_now, 10))[0];
        await _store.TryDispatchAsync(due, _now, (_, _) => Task.CompletedTask);

        // A retirement racing a successful dispatch must not rewrite a fired timer into a failed one.
        var act = async () => await _store.DeadLetterAsync(due.Id, _now.AddMinutes(1), "too late");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeadLetterAsync_RejectsAMissingReason()
    {
        var act = async () => await _store.DeadLetterAsync(1, _now, null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("reason");
    }

    [Fact]
    public async Task DeadLetterAsync_IgnoresAnUnknownTimeout()
    {
        var act = async () => await _store.DeadLetterAsync(999, _now, "gone");

        await act.Should().NotThrowAsync();
    }

    private static SagaTimeout Timeout(
        string name,
        DateTimeOffset dueAt,
        int stepIndex = 1,
        string correlationKey = "quote-1") => new()
        {
            SagaType = "Sales",
            CorrelationKey = correlationKey,
            Name = name,
            StepIndex = stepIndex,
            DueAt = dueAt,
            PayloadJson = "{}",
            PayloadTypeName = typeof(object).AssemblyQualifiedName!,
            ConcurrencyToken = Guid.NewGuid(),
        };
}
