using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.Execution;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Execution;

/// <summary>
/// Proves that every per-step DI scope a saga run creates is disposed once the awaited step has
/// finished, so sustained saga traffic does not leak scoped services (a DbContext among them) until
/// finalization. The async-only-disposable spy is the load-bearing part: a synchronously disposed
/// scope that still holds a resolved <see cref="IAsyncDisposable"/>-only service throws, so a green
/// run is proof the scope was awaited on disposal, not disposed synchronously.
/// </summary>
public class SagaScopeDisposalTests
{
    public record ItemAdded(string CartId);
    public record ItemShipped(string CartId);

    public class CartState
    {
        public int Steps { get; set; }
    }

    /// <summary>Shared, thread-safe tally of how many scoped spies were disposed.</summary>
    private sealed class DisposalLedger
    {
        private int _syncDisposed;
        private int _asyncDisposed;

        public int SyncDisposed => Volatile.Read(ref _syncDisposed);

        public int AsyncDisposed => Volatile.Read(ref _asyncDisposed);

        public void RecordSync() => Interlocked.Increment(ref _syncDisposed);

        public void RecordAsync() => Interlocked.Increment(ref _asyncDisposed);
    }

    /// <summary>Scoped service that flags its disposal on the shared ledger.</summary>
    private sealed class ScopedSpy : IDisposable
    {
        private readonly DisposalLedger _ledger;

        public ScopedSpy(DisposalLedger ledger) => _ledger = ledger;

        public void Dispose() => _ledger.RecordSync();
    }

    /// <summary>
    /// Scoped service that only implements <see cref="IAsyncDisposable"/>. Disposing the scope
    /// synchronously while this is resolved would throw; a clean run proves async disposal.
    /// </summary>
    private sealed class AsyncOnlySpy : IAsyncDisposable
    {
        private readonly DisposalLedger _ledger;

        public AsyncOnlySpy(DisposalLedger ledger) => _ledger = ledger;

        public ValueTask DisposeAsync()
        {
            _ledger.RecordAsync();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Two-step flow whose every step resolves both spies from the step scope.</summary>
    public class SpyResolvingFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<CartState>()
                .StartWith<ItemAdded>(e => e.CartId)
                .Then((ctx, e) =>
                {
                    ctx.Services.GetRequiredService<ScopedSpy>();
                    ctx.Services.GetRequiredService<AsyncOnlySpy>();
                    ctx.State.Steps++;
                    return Task.CompletedTask;
                })
                .WaitFor<ItemShipped>(e => e.CartId)
                .Then((ctx, e) =>
                {
                    ctx.Services.GetRequiredService<ScopedSpy>();
                    ctx.Services.GetRequiredService<AsyncOnlySpy>();
                    ctx.State.Steps++;
                    return Task.CompletedTask;
                })
                .Complete();
        }
    }

    private static (ServiceProvider Provider, DisposalLedger Ledger) BuildProvider()
    {
        var ledger = new DisposalLedger();
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        services.AddSingleton(ledger);
        services.AddScoped<ScopedSpy>();
        services.AddScoped<AsyncOnlySpy>();
        services.AddBsftSagaFlow<SpyResolvingFlow>();

        var provider = services.BuildServiceProvider();
        provider.UseBsftSagaFlows();
        return (provider, ledger);
    }

    [Fact]
    public async Task PublishAsync_WhenTriggerStepCompletes_DisposesTheScopeItCreated()
    {
        var (provider, ledger) = BuildProvider();
        using var sp = provider;
        var ea = sp.GetRequiredService<IEventAggregator>();

        await ea.PublishAsync(new ItemAdded("cart-1"));

        // The trigger scope is disposed once the awaited step and its commit have finished.
        ledger.SyncDisposed.Should().Be(1);
        ledger.AsyncDisposed.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_WhenSubsequentStepCompletes_DisposesItsOwnScope()
    {
        var (provider, ledger) = BuildProvider();
        using var sp = provider;
        var ea = sp.GetRequiredService<IEventAggregator>();

        await ea.PublishAsync(new ItemAdded("cart-2"));
        await ea.PublishAsync(new ItemShipped("cart-2"));

        // One scope per step: the trigger and the subsequent step each created and disposed one.
        ledger.SyncDisposed.Should().Be(2);
        ledger.AsyncDisposed.Should().Be(2);
    }

    [Fact]
    public async Task PublishAsync_UnderSustainedTraffic_LeavesNoScopeUndisposed()
    {
        const int events = 25;
        var (provider, ledger) = BuildProvider();
        using var sp = provider;
        var ea = sp.GetRequiredService<IEventAggregator>();

        for (var i = 0; i < events; i++)
            await ea.PublishAsync(new ItemAdded($"cart-{i}"));

        // Every published event started its own saga, so every trigger scope must have been disposed.
        ledger.SyncDisposed.Should().Be(events);
        ledger.AsyncDisposed.Should().Be(events);
    }

    [Fact]
    public async Task StartSagaAsync_WhenTriggerStepCompletes_DisposesTheScopeItCreated()
    {
        var (provider, ledger) = BuildProvider();
        using var sp = provider;
        var engine = sp.GetRequiredService<ISagaEngine>();

        var factory = new SagaBuilderFactory(engine);
        var definition = factory.WithState<CartState>()
            .StartWith<ItemAdded>(e => e.CartId)
            .Then((ctx, e) =>
            {
                ctx.Services.GetRequiredService<ScopedSpy>();
                ctx.Services.GetRequiredService<AsyncOnlySpy>();
                ctx.State.Steps++;
                return Task.CompletedTask;
            })
            .Complete();

        await engine.StartSagaAsync(definition, "cart-engine", new ItemAdded("cart-engine"));

        ledger.SyncDisposed.Should().Be(1);
        ledger.AsyncDisposed.Should().Be(1);
    }
}
