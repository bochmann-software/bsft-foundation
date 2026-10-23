using Bsft.Core.Concurrency;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests.Concurrency;

public sealed class KeyedGateTests
{
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(30);

    private readonly KeyedGate<string> _gate = new();

    [Fact]
    public async Task EnterAsync_SameKey_WaitsForTheHolder()
    {
        var held = await _gate.EnterAsync("a");

        var waiting = _gate.EnterAsync("a");

        waiting.IsCompleted.Should().BeFalse();
        held.Dispose();
        (await waiting.WaitAsync(Safety)).Dispose();
    }

    [Fact]
    public async Task EnterAsync_OtherKey_DoesNotWait()
    {
        using var held = await _gate.EnterAsync("a");

        var other = _gate.EnterAsync("b");

        other.IsCompletedSuccessfully.Should().BeTrue();
        (await other).Dispose();
    }

    [Fact]
    public async Task EnterAsync_KeysTheComparerEquates_AreTheSameKey()
    {
        var gate = new KeyedGate<string>(StringComparer.OrdinalIgnoreCase);
        var held = await gate.EnterAsync("Alpha");

        var waiting = gate.EnterAsync("aLPHA");

        waiting.IsCompleted.Should().BeFalse();
        held.Dispose();
        (await waiting.WaitAsync(Safety)).Dispose();
    }

    [Fact]
    public async Task EnterAsync_KeysOnlyTheDefaultComparerSeparates_AreDifferentKeys()
    {
        using var held = await _gate.EnterAsync("Alpha");

        var other = _gate.EnterAsync("alpha");

        other.IsCompletedSuccessfully.Should().BeTrue();
        (await other).Dispose();
    }

    [Fact]
    public async Task EnterAsync_Waiters_AreAdmittedInTheOrderTheyAsked()
    {
        var held = await _gate.EnterAsync("a");
        var first = _gate.EnterAsync("a");
        var second = _gate.EnterAsync("a");
        var third = _gate.EnterAsync("a");

        held.Dispose();
        var firstHeld = await first.WaitAsync(Safety);
        second.IsCompleted.Should().BeFalse();
        third.IsCompleted.Should().BeFalse();

        firstHeld.Dispose();
        var secondHeld = await second.WaitAsync(Safety);
        third.IsCompleted.Should().BeFalse();

        secondHeld.Dispose();
        (await third.WaitAsync(Safety)).Dispose();
    }

    [Fact]
    public async Task EnterAsync_CancelledWhileWaiting_ThrowsAndLeavesTheKeyUsable()
    {
        var held = await _gate.EnterAsync("a");
        using var cancellation = new CancellationTokenSource();

        var waiting = _gate.EnterAsync("a", cancellation.Token);
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => waiting).Should().ThrowAsync<OperationCanceledException>();
        held.Dispose();
        _gate.TrackedKeys.Should().Be(0);
        using var again = await _gate.EnterAsync("a").WaitAsync(Safety);
    }

    [Fact]
    public async Task EnterAsync_CancelledWaiterBetweenTwoOthers_IsSkipped()
    {
        var held = await _gate.EnterAsync("a");
        using var cancellation = new CancellationTokenSource();
        var first = _gate.EnterAsync("a");
        var cancelled = _gate.EnterAsync("a", cancellation.Token);
        var last = _gate.EnterAsync("a");
        await cancellation.CancelAsync();

        held.Dispose();
        var firstHeld = await first.WaitAsync(Safety);
        firstHeld.Dispose();

        (await last.WaitAsync(Safety)).Dispose();
        await FluentActions.Awaiting(() => cancelled).Should().ThrowAsync<OperationCanceledException>();
        _gate.TrackedKeys.Should().Be(0);
    }

    [Fact]
    public async Task EnterAsync_AlreadyCancelled_ThrowsWithoutTrackingTheKey()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => _gate.EnterAsync("a", cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();

        _gate.TrackedKeys.Should().Be(0);
    }

    [Fact]
    public async Task EnterAsync_DisposedTwice_FreesOnlyOnce()
    {
        var held = await _gate.EnterAsync("a");
        held.Dispose();
        var next = await _gate.EnterAsync("a");

        held.Dispose();

        _gate.EnterAsync("a").IsCompleted.Should().BeFalse("the second dispose must not free the gate of the next holder");
        next.Dispose();
    }

    [Fact]
    public async Task TrackedKeys_AfterEveryoneLeft_IsZero()
    {
        var first = await _gate.EnterAsync("a");
        var second = await _gate.EnterAsync("b");
        var waiting = _gate.EnterAsync("a");
        _gate.TrackedKeys.Should().Be(2);

        first.Dispose();
        (await waiting.WaitAsync(Safety)).Dispose();
        second.Dispose();

        _gate.TrackedKeys.Should().Be(0);
    }

    [Fact]
    public async Task EnterAsync_ManyCallersOverFewKeys_NeverOverlapAndLeaveNothingBehind()
    {
        // One slot per key, only ever touched by the callers of that key, so nothing here is shared
        // between keys: a lost update or an overlap shows up as a wrong count, not as a torn collection.
        string[] keys = ["a", "b", "c"];
        var counters = new int[keys.Length];
        var inside = new int[keys.Length];
        var overlaps = 0;

        await Task.WhenAll(Enumerable.Range(0, 90).Select(index => Task.Run(async () =>
        {
            var slot = index % keys.Length;
            using (await _gate.EnterAsync(keys[slot]))
            {
                if (inside[slot]++ != 0)
                    Interlocked.Increment(ref overlaps);
                var seen = counters[slot];
                await Task.Yield();
                counters[slot] = seen + 1;
                inside[slot]--;
            }
        }))).WaitAsync(Safety);

        overlaps.Should().Be(0);
        counters.Should().OnlyContain(count => count == 30);
        _gate.TrackedKeys.Should().Be(0);
    }

    [Fact]
    public async Task EnterAsync_NullKey_Throws() =>
        await FluentActions.Awaiting(() => new KeyedGate<string>().EnterAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
}
