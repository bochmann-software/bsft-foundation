using Bsft.Hosting.BackgroundWork;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Hosting.Tests.BackgroundWork;

public sealed class ServiceScopeFactoryExtensionsTests
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScopeFactory _scopeFactory;

    public ServiceScopeFactoryExtensionsTests()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedCounter>();
        _provider = services.BuildServiceProvider();
        _scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task RunScopedAsync_ResolvesTheServiceAndHandsOnTheToken()
    {
        using var cts = new CancellationTokenSource();
        ScopedCounter? seen = null;

        await _scopeFactory.RunScopedAsync<ScopedCounter>(
            (counter, ct) =>
            {
                seen = counter;
                ct.Should().Be(cts.Token);

                return Task.CompletedTask;
            },
            cts.Token);

        seen.Should().NotBeNull();
        seen!.IsDisposed.Should().BeTrue("the scope owns the service and is disposed after the operation");
    }

    [Fact]
    public async Task RunScopedAsync_EveryCallGetsAScopeOfItsOwn()
    {
        var first = await _scopeFactory.RunScopedAsync<ScopedCounter, ScopedCounter>((counter, _) => Task.FromResult(counter));
        var second = await _scopeFactory.RunScopedAsync<ScopedCounter, ScopedCounter>((counter, _) => Task.FromResult(counter));

        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public async Task RunScopedAsync_AFailingOperation_StillDisposesTheScope()
    {
        ScopedCounter? seen = null;

        var act = async () => await _scopeFactory.RunScopedAsync<ScopedCounter>((counter, _) =>
        {
            seen = counter;

            throw new InvalidOperationException("the work failed");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        seen!.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task RunScopedAsync_ResultOverload_ReturnsWhatTheOperationProduced()
    {
        var answer = await _scopeFactory.RunScopedAsync<ScopedCounter, int>((counter, _) => Task.FromResult(counter.Next()));

        answer.Should().Be(1);
    }

    [Fact]
    public void RunScoped_ResolvesTheServiceAndDisposesTheScope()
    {
        ScopedCounter? seen = null;

        _scopeFactory.RunScoped<ScopedCounter>(counter => seen = counter);

        seen!.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void RunScoped_ResultOverload_ReturnsWhatTheOperationProduced()
    {
        _scopeFactory.RunScoped<ScopedCounter, int>(counter => counter.Next()).Should().Be(1);
    }

    [Fact]
    public async Task TheNullGuardsRejectMissingArguments()
    {
        IServiceScopeFactory missing = null!;

        var missingAsync = async () => await missing.RunScopedAsync<ScopedCounter>((_, _) => Task.CompletedTask);
        await missingAsync.Should().ThrowAsync<ArgumentNullException>().WithParameterName("scopeFactory");
        await _scopeFactory.Invoking(f => f.RunScopedAsync<ScopedCounter>(null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("operation");

        var missingResultAsync = async () => await missing.RunScopedAsync<ScopedCounter, int>((_, _) => Task.FromResult(1));
        await missingResultAsync.Should().ThrowAsync<ArgumentNullException>().WithParameterName("scopeFactory");
        await _scopeFactory.Invoking(f => f.RunScopedAsync<ScopedCounter, int>(null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("operation");

        ((Action)(() => missing.RunScoped<ScopedCounter>(_ => { })))
            .Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
        ((Action)(() => _scopeFactory.RunScoped<ScopedCounter>(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("operation");

        ((Action)(() => missing.RunScoped<ScopedCounter, int>(_ => 1)))
            .Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
        ((Action)(() => _scopeFactory.RunScoped<ScopedCounter, int>(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("operation");
    }

    /// <summary>A scoped service that says whether the scope that owned it was disposed.</summary>
    private sealed class ScopedCounter : IDisposable
    {
        private int _count;

        public bool IsDisposed { get; private set; }

        public int Next() => ++_count;

        public void Dispose() => IsDisposed = true;
    }
}
