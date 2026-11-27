using Bsft.Outbox.DependencyInjection;
using Bsft.Outbox.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Outbox.Tests;

/// <summary>Tests the seam the worker runs around each drain pass.</summary>
public sealed class OutboxDrainPassTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(7);

    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly RecordingLogger<OutboxDrainWorker<string>> _logger = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<string> _events = [];

    [Fact]
    public async Task RunLoopAsync_WithAPass_RunsEachRoundInsideItAndDrainsWhileRowsAreApplied()
    {
        var applied = new Queue<int>([3, 0]);
        var drainer = new ScriptedDrainer((_, _) =>
        {
            _events.Add("drain");

            return Task.FromResult(applied.Dequeue());
        });
        var running = Worker(drainer, async (_, drainOnce, token) =>
        {
            _events.Add("before");
            var rows = await drainOnce(token);
            _events.Add("after");

            return rows;
        }).RunLoopAsync(_stop.Token);

        await _clock.WaitForTimerAsync(Interval);

        _events.Should().Equal("before", "drain", "after", "before", "drain", "after");

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WithAPass_GivesItAScopeOfItsOwnPerPassAndDisposesIt()
    {
        var scopes = new List<ScopedMarker>();
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(0));
        var running = Worker(drainer, (serviceProvider, drainOnce, token) =>
        {
            scopes.Add(serviceProvider.GetRequiredService<ScopedMarker>());

            return drainOnce(token);
        }).RunLoopAsync(_stop.Token);

        await _clock.WaitForTimerAsync(Interval);
        _clock.Advance(Interval);
        await drainer.StartedAsync(2);
        await _clock.WaitForTimerAsync(Interval);

        scopes.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        scopes.Should().OnlyContain(marker => marker.Disposed);

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenThePassDoesNotRunTheRound_LeasesNothingAndWaitsForTheInterval()
    {
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(5));
        var running = Worker(drainer, (_, _, _) => Task.FromResult(0)).RunLoopAsync(_stop.Token);

        await _clock.WaitForTimerAsync(Interval);

        drainer.Passes.Should().Be(0);

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenThePassFails_ReportsItLikeADrainFailureAndTriesAgain()
    {
        var calls = 0;
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(0));
        var running = Worker(drainer, (_, drainOnce, token) =>
            ++calls == 1 ? throw new InvalidOperationException("smtp down") : drainOnce(token)).RunLoopAsync(_stop.Token);

        await _logger.WrittenAsync(entry => entry.Level == LogLevel.Error);
        await _clock.WaitForTimerAsync(Interval);
        _clock.Advance(Interval);
        await drainer.StartedAsync(1);

        _logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error)
            .Which.Exception.Should().BeOfType<InvalidOperationException>();

        await StopAsync(running);
    }

    [Fact]
    public async Task RunLoopAsync_WhenTheHostStopsDuringThePass_HandsThePassTheGraceBoundedTokenAndStartsNoRound()
    {
        var tokens = new List<CancellationToken>();
        var release = new TaskCompletionSource();
        var drainer = new ScriptedDrainer((_, _) => Task.FromResult(4));
        var running = Worker(drainer, async (_, drainOnce, token) =>
        {
            tokens.Add(token);
            await release.Task;

            return await drainOnce(token);
        }).RunLoopAsync(_stop.Token);

        while (tokens.Count == 0)
            await Task.Yield();

        await _stop.CancelAsync();

        tokens[0].IsCancellationRequested.Should().BeFalse();

        release.SetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(30));

        drainer.Passes.Should().Be(1);
        tokens.Should().ContainSingle();
    }

    [Fact]
    public async Task RunLoopAsync_WhenTheHostStopsBeforeTheRound_TheRoundStartsNoRow()
    {
        var drainer = new RoundGateDrainer();
        var running = Worker(drainer, async (_, drainOnce, token) =>
        {
            await _stop.CancelAsync();

            return await drainOnce(token);
        }).RunLoopAsync(_stop.Token);

        await running.WaitAsync(TimeSpan.FromSeconds(30));

        drainer.MayStart.Should().BeFalse();
    }

    [Fact]
    public async Task AddBsftOutboxDrainPass_WithTheWorker_RunsThePassFromTheWorkerOfTheHost()
    {
        var calls = 0;
        using var provider = Provider(services =>
        {
            services.AddSingleton<Func<IServiceProvider, Func<CancellationToken, Task<int>>, CancellationToken, Task<int>>>((_, drainOnce, token) =>
            {
                calls++;

                return drainOnce(token);
            });
            services.AddBsftOutboxDrainPass<string, DelegatingPass>();
            services.AddBsftOutboxDrainPass<string, DelegatingPass>();
            services.AddBsftOutboxDrainWorker<string>("primary");
        });
        var worker = provider.GetRequiredKeyedService<OutboxDrainWorker<string>>("primary");
        var running = worker.RunLoopAsync(_stop.Token);

        await _clock.WaitForTimerAsync(Interval);

        calls.Should().Be(1);
        provider.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IOutboxDrainPass<string>)).Should().BeTrue();

        await StopAsync(running);
    }

    [Fact]
    public async Task AddBsftOutboxDrainWorker_WithoutAPass_DrainsWithoutCreatingAScope()
    {
        using var provider = Provider(services =>
        {
            services.AddSingleton<IServiceScopeFactory, ThrowingScopeFactory>();
            services.AddBsftOutboxDrainWorker<string>("primary");
        });
        var running = provider.GetRequiredKeyedService<OutboxDrainWorker<string>>("primary").RunLoopAsync(_stop.Token);

        await _clock.WaitForTimerAsync(Interval);

        running.IsFaulted.Should().BeFalse();

        await StopAsync(running);
    }

    [Fact]
    public void AddBsftOutboxDrainPass_WhenServicesIsNull_Throws()
    {
        var act = () => OutboxServiceCollectionExtensions.AddBsftOutboxDrainPass<string, DelegatingPass>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    private ServiceProvider Provider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IOutboxStore<string>>(new RecordingOutboxStore());
        services.AddSingleton<IOutboxSideEffect<string>>(new ScriptedSideEffect());
        services.Configure<BsftOutboxOptions>("primary", options => options.PollInterval = Interval);

        configure(services);

        return services.BuildServiceProvider();
    }

    private OutboxDrainWorker<string> Worker(
        IOutboxDrainer<string> drainer,
        Func<IServiceProvider, Func<CancellationToken, Task<int>>, CancellationToken, Task<int>> pass)
    {
        var services = new ServiceCollection();

        services.AddScoped<ScopedMarker>();
        services.AddScoped<IOutboxDrainPass<string>>(_ => new DelegatingPass(pass));

        var provider = services.BuildServiceProvider();

        return new OutboxDrainWorker<string>(
            drainer,
            Options.Create(new BsftOutboxOptions { PollInterval = Interval }),
            Options.Create(new BsftOutboxDrainWorkerOptions { ShutdownGrace = TimeSpan.FromSeconds(30) }),
            null,
            _clock,
            _logger,
            provider.GetRequiredService<IServiceScopeFactory>());
    }

    private async Task StopAsync(Task running)
    {
        await _stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class ScopedMarker : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class DelegatingPass : IOutboxDrainPass<string>
    {
        private readonly Func<IServiceProvider, Func<CancellationToken, Task<int>>, CancellationToken, Task<int>> _run;

        public DelegatingPass(Func<IServiceProvider, Func<CancellationToken, Task<int>>, CancellationToken, Task<int>> run) =>
            _run = run;

        public Task<int> RunAsync(
            IServiceProvider serviceProvider,
            Func<CancellationToken, Task<int>> drainOnce,
            CancellationToken cancellationToken) =>
            _run(serviceProvider, drainOnce, cancellationToken);
    }

    private sealed class RoundGateDrainer : IOutboxDrainer<string>
    {
        public bool? MayStart { get; private set; }

        public Task<int> DrainOnceAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<int> DrainOnceAsync(Func<bool> mayStartRow, CancellationToken cancellationToken = default)
        {
            MayStart = mayStartRow();

            return Task.FromResult(0);
        }
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("No scope may be created without a pass.");
    }
}
