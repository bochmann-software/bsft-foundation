using Bsft.Core.Retry;
using Bsft.Outbox.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bsft.Outbox.Tests;

/// <summary>Tests the drain pass every outbox in a host shares.</summary>
public class OutboxDrainerTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly RecordingOutboxStore _store = new();
    private readonly ScriptedSideEffect _sideEffect = new();
    private readonly MovableTimeProvider _time = new(Start);

    [Fact]
    public async Task A_pass_applies_every_leased_row_and_marks_it()
    {
        Stage(1, "first");
        Stage(2, "second");

        var applied = await Drainer().DrainOnceAsync();

        applied.Should().Be(2);
        _sideEffect.Applied.Should().Equal("first", "second");
        _store.Processed.Should().Equal(1, 2);
    }

    [Fact]
    public async Task A_pass_over_an_empty_outbox_applies_nothing()
    {
        (await Drainer().DrainOnceAsync()).Should().Be(0);

        _sideEffect.Applied.Should().BeEmpty();
    }

    /// <summary>The lease is asked for with the configured bounds, which is what the store claims on.</summary>
    [Fact]
    public async Task A_pass_leases_with_the_configured_bounds()
    {
        await Drainer(o => o.BatchSize = 7).DrainOnceAsync();

        _store.Leases.Should().ContainSingle()
            .Which.Should().Be((Start, TimeSpan.FromMinutes(1), 7));
    }

    [Fact]
    public async Task A_failed_row_is_handed_back_with_the_instant_the_schedule_produced()
    {
        Stage(1, "boom");
        Fail("boom", new InvalidOperationException("transport down"));

        var applied = await Drainer(o => o.RetryBackoff = TimeSpan.FromSeconds(30)).DrainOnceAsync();

        applied.Should().Be(0);
        _store.Processed.Should().BeEmpty();
        _store.Abandoned.Should().ContainSingle()
            .Which.Should().Be((1L, Start + TimeSpan.FromSeconds(30)));
    }

    /// <summary>A wait the failure asked for wins over the schedule's own, up to the ceiling.</summary>
    [Fact]
    public async Task A_failure_that_asks_for_a_wait_is_obeyed()
    {
        Stage(1, "boom");
        Fail("boom", new RetryAfterException("throttled", TimeSpan.FromMinutes(2)));

        await Drainer(o => o.RetryBackoff = TimeSpan.FromSeconds(1)).DrainOnceAsync();

        _store.Abandoned.Should().ContainSingle()
            .Which.NextAttemptAt.Should().Be(Start + TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task A_row_that_has_used_up_its_attempts_is_retired_with_the_reason_it_failed()
    {
        Stage(1, "boom", attemptCount: 2);
        Fail("boom", new InvalidOperationException("transport down"));

        await Drainer(o => o.MaxDeliveryAttempts = 3).DrainOnceAsync();

        _store.Abandoned.Should().BeEmpty();
        _store.DeadLettered.Should().ContainSingle()
            .Which.Should().Be((1L, "transport down"));
    }

    /// <summary>
    /// Several failures at once say nothing on their own, so the aggregate's own text is recorded
    /// rather than one arbitrary inner message.
    /// </summary>
    [Fact]
    public async Task A_row_retired_after_several_failures_at_once_records_the_whole_failure()
    {
        Stage(1, "boom", attemptCount: 2);
        _sideEffect.Outcomes["boom"] = () => Task.FromException(new AggregateException(
            new InvalidOperationException("first leg"), new InvalidOperationException("second leg")));

        await Drainer(o => o.MaxDeliveryAttempts = 3).DrainOnceAsync();

        _store.DeadLettered.Should().ContainSingle()
            .Which.Reason.Should().NotBe("first leg");
    }

    /// <summary>
    /// A side effect cancelled by a token of its own is a failed attempt, not a shutdown. Taking it
    /// for one would let it unwind through the pass and end the loop above.
    /// </summary>
    [Fact]
    public async Task A_side_effect_cancelled_by_its_own_token_is_a_failed_attempt()
    {
        Stage(1, "slow");
        _sideEffect.Outcomes["slow"] = () => Task.FromCanceled(new CancellationToken(canceled: true));

        var applied = await Drainer().DrainOnceAsync();

        applied.Should().Be(0);
        _store.Abandoned.Should().ContainSingle();
    }

    /// <summary>
    /// A shutdown is not a failed attempt: it unwinds the pass, and the row stays pending for the
    /// instance that picks the outbox up next.
    /// </summary>
    [Fact]
    public async Task A_side_effect_cancelled_by_the_shutdown_unwinds_the_pass()
    {
        Stage(1, "slow");
        _sideEffect.Outcomes["slow"] = () => Task.FromCanceled(new CancellationToken(canceled: true));

        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        var act = async () => await Drainer().DrainOnceAsync(stopping.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _store.Abandoned.Should().BeEmpty();
        _store.DeadLettered.Should().BeEmpty();
    }

    /// <summary>
    /// A side effect that throws before its first await throws synchronously. It has to reach the
    /// same failure handling as any other, rather than unwinding the whole pass.
    /// </summary>
    [Fact]
    public async Task A_side_effect_that_throws_synchronously_is_a_failed_attempt()
    {
        Stage(1, "boom");
        _sideEffect.Outcomes["boom"] = () => throw new InvalidOperationException("argument refused");

        var applied = await Drainer().DrainOnceAsync();

        applied.Should().Be(0);
        _store.Abandoned.Should().ContainSingle();
    }

    /// <summary>
    /// A row applied under a lease that has since expired is one of the duplicates at-least-once
    /// permits: the effect stands, and the pass does not count it as its own.
    /// </summary>
    [Fact]
    public async Task A_row_whose_lease_expired_before_the_mark_is_not_counted()
    {
        Stage(1, "first");
        _store.MarkSucceeds = false;

        var applied = await Drainer().DrainOnceAsync();

        applied.Should().Be(0);
        _sideEffect.Applied.Should().Equal("first");
    }

    /// <summary>
    /// The batch is leased until one instant. Once it has passed, the rest of the batch stays pending
    /// rather than being applied under a lease this drainer no longer holds. The first row is always
    /// attempted, or a slow lease would make every pass do nothing at all.
    /// </summary>
    [Fact]
    public async Task A_batch_that_outlives_its_lease_stops_rather_than_applying_under_it()
    {
        Stage(1, "first");
        Stage(2, "second");
        _sideEffect.Outcomes["first"] = () =>
        {
            _time.Advance(TimeSpan.FromMinutes(2));

            return Task.CompletedTask;
        };

        var applied = await Drainer().DrainOnceAsync();

        applied.Should().Be(1);
        _sideEffect.Applied.Should().Equal("first");
    }

    [Theory]
    [InlineData("BatchSize")]
    [InlineData("PollInterval")]
    [InlineData("LeaseDuration")]
    [InlineData("MaxDeliveryAttempts")]
    [InlineData("RetryBackoff")]
    [InlineData("MaxRetryDelay")]
    public void An_unusable_bound_is_refused_at_construction(string option)
    {
        var act = () => Drainer(o => Break(o, option));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(option);
    }

    [Fact]
    public void A_missing_dependency_is_rejected()
    {
        var options = new BsftOutboxOptions();
        var schedule = new OutboxRetrySchedule(Random.Shared);
        var logger = NullLogger.Instance;

        var withoutStore = () => new OutboxDrainer<string>(null!, _sideEffect, schedule, options, _time, logger);
        var withoutEffect = () => new OutboxDrainer<string>(_store, null!, schedule, options, _time, logger);
        var withoutSchedule = () => new OutboxDrainer<string>(_store, _sideEffect, null!, options, _time, logger);
        var withoutOptions = () => new OutboxDrainer<string>(_store, _sideEffect, schedule, null!, _time, logger);
        var withoutTime = () => new OutboxDrainer<string>(_store, _sideEffect, schedule, options, null!, logger);
        var withoutLogger = () => new OutboxDrainer<string>(_store, _sideEffect, schedule, options, _time, null!);

        withoutStore.Should().Throw<ArgumentNullException>().WithParameterName("store");
        withoutEffect.Should().Throw<ArgumentNullException>().WithParameterName("sideEffect");
        withoutSchedule.Should().Throw<ArgumentNullException>().WithParameterName("retrySchedule");
        withoutOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        withoutTime.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        withoutLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    private static void Break(BsftOutboxOptions options, string member)
    {
        switch (member)
        {
            case "BatchSize":
                options.BatchSize = 0;
                break;
            case "PollInterval":
                options.PollInterval = TimeSpan.Zero;
                break;
            case "LeaseDuration":
                options.LeaseDuration = TimeSpan.Zero;
                break;
            case "MaxDeliveryAttempts":
                options.MaxDeliveryAttempts = 0;
                break;
            case "RetryBackoff":
                options.RetryBackoff = TimeSpan.FromSeconds(-1);
                break;
            default:
                options.MaxRetryDelay = TimeSpan.Zero;
                break;
        }
    }

    private void Stage(long id, string payload, int attemptCount = 0) =>
        _store.Pending.Add(new LeasedOutboxRow<string>(id, payload, attemptCount, Guid.NewGuid()));

    private void Fail(string payload, Exception failure) =>
        _sideEffect.Outcomes[payload] = () => Task.FromException(failure);

    private OutboxDrainer<string> Drainer(Action<BsftOutboxOptions>? configure = null)
    {
        var options = new BsftOutboxOptions();

        configure?.Invoke(options);

        return new OutboxDrainer<string>(
            _store,
            _sideEffect,
            new OutboxRetrySchedule(new ZeroRandom()),
            options,
            _time,
            NullLogger.Instance);
    }

    /// <summary>Randomness that always answers zero, so an interval is its plain value.</summary>
    private sealed class ZeroRandom : Random
    {
        public override double NextDouble() => 0;
    }
}
