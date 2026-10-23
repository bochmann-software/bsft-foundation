using Bsft.Core.Concurrency;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests.Concurrency;

public sealed class SlotPoolTests
{
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(30);

    [Fact]
    public void SlotPool_CapacityBelowOne_Throws() =>
        FluentActions.Invoking(() => new SlotPool(0)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public async Task EnterAsync_UpToTheCapacity_DoesNotWait()
    {
        var pool = new SlotPool(2);

        var first = pool.EnterAsync();
        var second = pool.EnterAsync();
        var third = pool.EnterAsync();

        pool.Capacity.Should().Be(2);
        first.IsCompletedSuccessfully.Should().BeTrue();
        second.IsCompletedSuccessfully.Should().BeTrue();
        third.IsCompleted.Should().BeFalse();
        pool.Available.Should().Be(0);
        (await first).Dispose();
        (await third.WaitAsync(Safety)).Dispose();
        (await second).Dispose();
        pool.Available.Should().Be(2);
    }

    [Fact]
    public async Task EnterAsync_Waiters_AreAdmittedInTheOrderTheyAsked()
    {
        var pool = new SlotPool(1);
        var held = await pool.EnterAsync();
        var first = pool.EnterAsync();
        var second = pool.EnterAsync();
        var third = pool.EnterAsync();

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
    public async Task EnterAsync_NewCaller_DoesNotOvertakeAWaiter()
    {
        var pool = new SlotPool(1);
        var held = await pool.EnterAsync();
        var waiting = pool.EnterAsync();

        held.Dispose();
        var newcomer = pool.EnterAsync();

        var waitingHeld = await waiting.WaitAsync(Safety);
        newcomer.IsCompleted.Should().BeFalse("the slot went to the caller that asked first");
        waitingHeld.Dispose();
        (await newcomer.WaitAsync(Safety)).Dispose();
    }

    [Fact]
    public async Task EnterAsync_CancelledWhileWaiting_ThrowsAndKeepsTheSlotCount()
    {
        var pool = new SlotPool(1);
        var held = await pool.EnterAsync();
        using var cancellation = new CancellationTokenSource();
        var waiting = pool.EnterAsync(cancellation.Token);
        var last = pool.EnterAsync();

        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => waiting).Should().ThrowAsync<OperationCanceledException>();
        held.Dispose();
        (await last.WaitAsync(Safety)).Dispose();
        pool.Available.Should().Be(1);
    }

    [Fact]
    public async Task EnterAsync_AlreadyCancelled_ThrowsEvenWithAFreeSlot()
    {
        var pool = new SlotPool(1);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => pool.EnterAsync(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();

        pool.Available.Should().Be(1);
    }

    [Fact]
    public async Task EnterAsync_CancelledAfterItWasGranted_StillHoldsTheSlot()
    {
        var pool = new SlotPool(1);
        var held = await pool.EnterAsync();
        using var cancellation = new CancellationTokenSource();
        var waiting = pool.EnterAsync(cancellation.Token);

        held.Dispose();
        await cancellation.CancelAsync();

        var granted = await waiting.WaitAsync(Safety);
        pool.Available.Should().Be(0);
        granted.Dispose();
        pool.Available.Should().Be(1);
    }

    [Fact]
    public async Task Slot_DisposedTwice_FreesOnlyOnce()
    {
        var pool = new SlotPool(1);
        var slot = await pool.EnterAsync();
        slot.Dispose();
        var next = await pool.EnterAsync();

        slot.Dispose();

        pool.Available.Should().Be(0);
        next.Dispose();
        pool.Available.Should().Be(1);
    }
}
