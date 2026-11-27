using Bsft.Core.Hosting;
using Bsft.Tests.Shared;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests.Hosting;

public sealed class HostedWorkStartGateExtensionsTests
{
    [Fact]
    public void WaitAllAsync_WithNullGates_IsAlreadyCompleted() =>
        ((IEnumerable<IHostedWorkStartGate>?)null).WaitAllAsync().IsCompletedSuccessfully.Should().BeTrue();

    [Fact]
    public void WaitAllAsync_WithNoGates_IsAlreadyCompleted() =>
        new List<IHostedWorkStartGate>().WaitAllAsync().IsCompletedSuccessfully.Should().BeTrue();

    [Fact]
    public async Task WaitAllAsync_WithSeveralGates_AwaitsThemInOrderAndCompletesWithTheLast()
    {
        var first = new ControllableStartGate();
        var second = new ControllableStartGate();

        var wait = new[] { first, second }.WaitAllAsync();
        await first.Awaited;

        second.WaitCount.Should().Be(0);
        first.Open();
        await second.Awaited;
        wait.IsCompleted.Should().BeFalse();

        second.Open();
        await wait;
    }

    [Fact]
    public async Task WaitAllAsync_WhenCancelledWhilePending_ThrowsOperationCanceled()
    {
        using var stopping = new CancellationTokenSource();
        var gate = new ControllableStartGate();

        var wait = new[] { gate }.WaitAllAsync(stopping.Token);
        await gate.Awaited;
        await stopping.CancelAsync();

        await wait.Invoking(w => w).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HasOpenedAsync_WhenTheGatesOpen_AnswersTrue()
    {
        var gate = new ControllableStartGate();
        gate.Open();

        (await new[] { gate }.HasOpenedAsync(CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task HasOpenedAsync_WithNoGates_AnswersTrue() =>
        (await ((IEnumerable<IHostedWorkStartGate>?)null).HasOpenedAsync(CancellationToken.None)).Should().BeTrue();

    [Fact]
    public async Task HasOpenedAsync_WhenStoppedWhilePending_AnswersFalse()
    {
        using var stopping = new CancellationTokenSource();
        var gate = new ControllableStartGate();

        var wait = new[] { gate }.HasOpenedAsync(stopping.Token);
        await gate.Awaited;
        await stopping.CancelAsync();

        (await wait).Should().BeFalse();
    }

    [Fact]
    public async Task HasOpenedAsync_WhenAGateCancelsOnATokenOfItsOwn_Throws()
    {
        var gate = new CancellingGate();

        await new[] { gate }.Invoking(g => g.HasOpenedAsync(CancellationToken.None))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class CancellingGate : IHostedWorkStartGate
    {
        public Task WaitAsync(CancellationToken cancellationToken) => Task.FromCanceled(new CancellationToken(true));
    }
}
