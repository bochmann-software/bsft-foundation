using Bsft.Core.Hosting;
using Bsft.Hosting.StartGates.Tests.Fakes;
using Bsft.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Hosting;

namespace Bsft.Hosting.StartGates.Tests;

public sealed class GatedHostedServiceTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly RecordingLoop _loop = new();

    private static GatedHostedService Gated(IHostedService inner, IReadOnlyList<IHostedWorkStartGate> gates) =>
        new(inner, gates, NullLogger.Instance);

    [Fact]
    public async Task StartAsync_WhenAGateIsPending_ReturnsAtOnceAndLeavesTheInnerServiceUntouched()
    {
        var gate = new ControllableStartGate();
        using var gated = Gated(_loop, [gate]);

        await gated.StartAsync(CancellationToken.None);
        await gate.Awaited;

        _loop.Starts.Should().Be(0);
        await Cleanup(gated);
    }

    [Fact]
    public async Task StartAsync_WhenTheGateOpens_StartsTheInnerService()
    {
        var gate = new ControllableStartGate();
        using var gated = Gated(_loop, [gate]);
        await gated.StartAsync(CancellationToken.None);
        await gate.Awaited;

        gate.Open();
        await _loop.Running.WaitAsync(Patience);

        _loop.Starts.Should().Be(1);
        await Cleanup(gated);
    }

    [Fact]
    public async Task StartAsync_WithSeveralGates_AwaitsThemInRegistrationOrder()
    {
        var awaited = new List<string>();
        using var gated = Gated(
            _loop, [new OrderRecordingGate("first", awaited), new OrderRecordingGate("second", awaited)]);

        await gated.StartAsync(CancellationToken.None);
        await _loop.Running.WaitAsync(Patience);

        awaited.Should().Equal("first", "second");
        await Cleanup(gated);
    }

    [Fact]
    public async Task StopAsync_WhenAGateIsPending_EndsCleanlyWithoutStoppingTheInnerService()
    {
        var gate = new ControllableStartGate();
        using var gated = Gated(_loop, [gate]);
        await gated.StartAsync(CancellationToken.None);
        await gate.Awaited;

        await gated.StopAsync(CancellationToken.None);

        _loop.Starts.Should().Be(0);
        _loop.Stops.Should().Be(0);
        gated.ExecuteTask!.Status.Should().Be(TaskStatus.RanToCompletion);
    }

    [Fact]
    public async Task StopAsync_WhenTheInnerServiceStarted_StopsIt()
    {
        var gate = new ControllableStartGate();
        using var gated = Gated(_loop, [gate]);
        await gated.StartAsync(CancellationToken.None);
        gate.Open();
        await _loop.Running.WaitAsync(Patience);

        await gated.StopAsync(CancellationToken.None);

        _loop.Stops.Should().Be(1);
        gated.ExecuteTask!.Status.Should().Be(TaskStatus.RanToCompletion);
    }

    [Fact]
    public async Task ExecuteTask_WhenAGateThrows_Faults()
    {
        using var gated = Gated(_loop, [new FaultingGate()]);

        await gated.StartAsync(CancellationToken.None);
        var act = () => gated.ExecuteTask!;

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("the schema is not reachable");
        _loop.Starts.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteTask_WhenTheInnerLoopFaultsAfterTheGates_Faults()
    {
        _loop.Failure = new InvalidOperationException("the loop broke");
        using var gated = Gated(_loop, [new OrderRecordingGate("open", [])]);

        await gated.StartAsync(CancellationToken.None);
        var act = () => gated.ExecuteTask!;

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("the loop broke");
    }

    [Fact]
    public async Task ExecuteTask_WhenTheInnerServiceIsNotABackgroundService_StartsItAndStopsItWithTheHost()
    {
        var plain = new PlainHostedService();
        using var gated = Gated(plain, [new OrderRecordingGate("open", [])]);

        await gated.StartAsync(CancellationToken.None);
        await gated.ExecuteTask!.WaitAsync(Patience);

        plain.Starts.Should().Be(1);

        await gated.StopAsync(CancellationToken.None);

        plain.Stops.Should().Be(1);
        gated.ExecuteTask.Status.Should().Be(TaskStatus.RanToCompletion);
    }

    [Fact]
    public async Task ExecuteTask_WhenTheInnerLoopEndsOnItsOwn_Completes()
    {
        var finishing = new FinishingLoop();
        using var gated = Gated(finishing, [new OrderRecordingGate("open", [])]);

        await gated.StartAsync(CancellationToken.None);
        await gated.ExecuteTask!.WaitAsync(Patience);

        gated.ExecuteTask.Status.Should().Be(TaskStatus.RanToCompletion);
    }

    [Fact]
    public async Task StartAsync_ForAServiceInTheHostLifecycle_GivesItsStartingAndStartedCallsAfterTheGate()
    {
        var gate = new ControllableStartGate();
        var lifecycle = new LifecycleLoop();
        using var gated = Gated(lifecycle, [gate]);

        await gated.StartingAsync(CancellationToken.None);
        await gated.StartAsync(CancellationToken.None);
        await gated.StartedAsync(CancellationToken.None);
        await gate.Awaited;
        lifecycle.Calls.Should().BeEmpty();

        gate.Open();
        await WaitUntilAsync(() => lifecycle.Calls.Count == 3);

        lifecycle.Calls.Should().Equal("starting", "start", "started");
        await gated.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_ForAServiceInTheHostLifecycle_ForwardsTheStopCallsOnceItStarted()
    {
        var lifecycle = new LifecycleLoop();
        using var gated = Gated(lifecycle, [new OrderRecordingGate("open", [])]);
        await gated.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => lifecycle.Calls.Count == 3);

        await gated.StoppingAsync(CancellationToken.None);
        await gated.StopAsync(CancellationToken.None);
        await gated.StoppedAsync(CancellationToken.None);

        lifecycle.Calls.Should().Equal("starting", "start", "started", "stopping", "stop", "stopped");
    }

    [Fact]
    public async Task StopAsync_ForAServiceInTheHostLifecycleThatNeverStarted_ForwardsNothing()
    {
        var gate = new ControllableStartGate();
        var lifecycle = new LifecycleLoop();
        using var gated = Gated(lifecycle, [gate]);
        await gated.StartAsync(CancellationToken.None);
        await gate.Awaited;

        await gated.StoppingAsync(CancellationToken.None);
        await gated.StopAsync(CancellationToken.None);
        await gated.StoppedAsync(CancellationToken.None);

        lifecycle.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task StopAsync_ForAServiceOutsideTheHostLifecycle_StopsWithoutLifecycleCalls()
    {
        var plain = new PlainHostedService();
        using var gated = Gated(plain, [new OrderRecordingGate("open", [])]);
        await gated.StartAsync(CancellationToken.None);
        await gated.ExecuteTask!.WaitAsync(Patience);

        await gated.StoppingAsync(CancellationToken.None);
        await gated.StopAsync(CancellationToken.None);
        await gated.StoppedAsync(CancellationToken.None);

        plain.Stops.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_WhenAGateIsPending_LogsOnceThatTheServiceNeverStarted()
    {
        var gate = new ControllableStartGate();
        var logger = new RecordingLogger<GatedHostedService>();
        using var gated = new GatedHostedService(_loop, [gate], logger);
        await gated.StartAsync(CancellationToken.None);
        await gate.Awaited;

        await gated.StopAsync(CancellationToken.None);

        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains(typeof(RecordingLoop).FullName!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopAsync_WhenTheStoppingCallCameBeforeTheServiceStarted_ForwardsItAheadOfTheStop()
    {
        var gate = new ControllableStartGate();
        var lifecycle = new LifecycleLoop();
        using var gated = Gated(lifecycle, [gate]);
        await gated.StartAsync(CancellationToken.None);
        await gate.Awaited;

        await gated.StoppingAsync(CancellationToken.None);
        gate.Open();
        await WaitUntilAsync(() => lifecycle.Calls.Count == 3);
        await gated.StopAsync(CancellationToken.None);

        lifecycle.Calls.Should().Equal("starting", "start", "started", "stopping", "stop");
    }

    [Fact]
    public async Task StopAsync_WhenTheStoppingCallWasForwarded_DoesNotForwardItAgain()
    {
        var lifecycle = new LifecycleLoop();
        using var gated = Gated(lifecycle, [new OrderRecordingGate("open", [])]);
        await gated.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => lifecycle.Calls.Count == 3);

        await gated.StoppingAsync(CancellationToken.None);
        await gated.StopAsync(CancellationToken.None);

        lifecycle.Calls.Where(call => call == "stopping").Should().ContainSingle();
    }

    [Fact]
    public void Dispose_DisposesTheInnerService()
    {
        var plain = new PlainHostedService();
        var gated = Gated(plain, []);

        gated.Dispose();

        plain.Disposed.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithAMissingArgument_Throws()
    {
        var cases = new (string Parameter, Action Call)[]
        {
            ("inner", () => Gated(null!, [])),
            ("gates", () => Gated(_loop, null!)),
            ("logger", () => new GatedHostedService(_loop, [], null!)),
        };

        foreach (var (parameter, call) in cases)
            call.Should().Throw<ArgumentNullException>().WithParameterName(parameter);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        condition().Should().BeTrue();
    }

    private static async Task Cleanup(GatedHostedService gated) => await gated.StopAsync(CancellationToken.None);

    private sealed class FinishingLoop : Microsoft.Extensions.Hosting.BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }
}
