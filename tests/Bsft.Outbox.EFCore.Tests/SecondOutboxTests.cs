using Bsft.Outbox.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bsft.Outbox.EFCore.Tests;

/// <summary>
/// Runs a whole second outbox — commit the intent, apply the side effect later — on the shared drainer
/// and the shared EF Core lease protocol, with nothing of the integration-event outbox involved.
/// </summary>
public sealed class SecondOutboxTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 8, 0, 0, TimeSpan.Zero);

    private readonly ProvisioningFixture _outbox = new();
    private readonly RecordingProvisioning _sideEffect = new();

    public void Dispose() => _outbox.Dispose();

    [Fact]
    public async Task A_committed_intent_is_carried_out_by_the_shared_drainer_and_then_marked()
    {
        var id = await _outbox.GivenIntentAsync("acme");

        var applied = await Drainer().DrainOnceAsync();

        applied.Should().Be(1);
        _sideEffect.Provisioned.Should().Equal("acme");
        (await _outbox.ReadRowAsync(id)).ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_carried_out_intent_is_not_carried_out_a_second_time()
    {
        await _outbox.GivenIntentAsync("acme");
        var drainer = Drainer();

        await drainer.DrainOnceAsync();
        var second = await drainer.DrainOnceAsync();

        second.Should().Be(0);
        _sideEffect.Provisioned.Should().Equal("acme");
    }

    [Fact]
    public async Task An_intent_whose_side_effect_failed_is_held_back_and_then_carried_out()
    {
        var id = await _outbox.GivenIntentAsync("acme");
        _sideEffect.Failures["acme"] = new InvalidOperationException("the provisioner is down");

        (await Drainer().DrainOnceAsync()).Should().Be(0);
        var row = await _outbox.ReadRowAsync(id);
        row.AttemptCount.Should().Be(1);
        row.NextAttemptAt.Should().NotBeNull();

        _sideEffect.Failures.Clear();
        var later = Drainer(row.NextAttemptAt!.Value);

        (await later.DrainOnceAsync()).Should().Be(1);
        _sideEffect.Provisioned.Should().Equal("acme");
    }

    [Fact]
    public async Task An_intent_that_keeps_failing_is_retired_with_the_reason_it_failed()
    {
        var id = await _outbox.GivenIntentAsync("acme");
        _sideEffect.Failures["acme"] = new InvalidOperationException("the tenant no longer exists");

        await Drainer(options: o => o.MaxDeliveryAttempts = 1).DrainOnceAsync();

        var row = await _outbox.ReadRowAsync(id);
        row.DeadLetteredAt.Should().NotBeNull();
        row.FailureReason.Should().Be("the tenant no longer exists");
        row.ProcessedAt.Should().BeNull();
    }

    private IOutboxDrainer<string> Drainer(DateTime? now = null, Action<BsftOutboxOptions>? options = null)
    {
        var drainOptions = new BsftOutboxOptions();

        options?.Invoke(drainOptions);

        return new OutboxDrainer<string>(
            new EFCoreOutboxStore<ProvisioningIntentRow, string>(
                _outbox.Scopes, new ProvisioningIntentRowSource()),
            _sideEffect,
            new OutboxRetrySchedule(Random.Shared),
            drainOptions,
            new FixedTimeProvider(now is { } instant ? new DateTimeOffset(instant, TimeSpan.Zero) : Now),
            NullLogger.Instance);
    }


    /// <summary>A clock the drain pass reads a fixed instant from.</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
