using Bsft.Scheduling.Heartbeats;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests.Heartbeats;

public sealed class InMemoryJobHeartbeatStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);

    private readonly FixedTimeProvider _time = new(Now);

    [Fact]
    public void TheStoreObservesFromTheInstantItWasBuilt()
    {
        new InMemoryJobHeartbeatStore(_time).ObservingSince.Should().Be(Now);
    }

    [Fact]
    public async Task AJobThatHasNotCompletedAnythingHasNoHeartbeat()
    {
        (await new InMemoryJobHeartbeatStore(_time).LastCompletedAtAsync("nightly")).Should().BeNull();
    }

    [Fact]
    public async Task ACompletedRunIsStampedFromTheStoresOwnClock()
    {
        var store = new InMemoryJobHeartbeatStore(_time);
        _time.Advance(TimeSpan.FromMinutes(5));

        await store.RecordCompletedAsync("nightly");

        (await store.LastCompletedAtAsync("nightly")).Should().Be(Now.AddMinutes(5));
        (await store.LastCompletedAtAsync("hourly")).Should().BeNull();
    }

    [Fact]
    public async Task TwoRunsSettlingOutOfOrderLeaveTheLaterHeartbeatStanding()
    {
        var store = new InMemoryJobHeartbeatStore(_time);
        _time.Advance(TimeSpan.FromMinutes(10));
        await store.RecordCompletedAsync("nightly");

        _time.Now = Now.AddMinutes(2);
        await store.RecordCompletedAsync("nightly");

        (await store.LastCompletedAtAsync("nightly")).Should().Be(Now.AddMinutes(10));
    }

    [Fact]
    public async Task TheNullGuardsRejectMissingArguments()
    {
        ((Action)(() => new InMemoryJobHeartbeatStore(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");

        var store = new InMemoryJobHeartbeatStore(_time);

        await store.Invoking(s => s.RecordCompletedAsync(null!)).Should()
            .ThrowAsync<ArgumentNullException>().WithParameterName("jobName");
        await store.Invoking(s => s.LastCompletedAtAsync(null!)).Should()
            .ThrowAsync<ArgumentNullException>().WithParameterName("jobName");
    }
}
