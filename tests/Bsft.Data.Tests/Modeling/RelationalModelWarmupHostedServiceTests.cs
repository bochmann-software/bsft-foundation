using System.Diagnostics;
using Bsft.Data.Modeling;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Tests.Modeling;

/// <summary>
/// When the service listens: from <c>StartingAsync</c> until it is stopped, and not at all when the
/// option turns it off. Contexts of other tests running in parallel reach the same diagnostic
/// listener, so every assertion is about the test's own context, never about a count.
/// </summary>
public sealed class RelationalModelWarmupHostedServiceTests
{
    [Fact]
    public void WithNullOptionsThrows()
    {
        var act = () => new RelationalModelWarmupHostedService(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithNullWarmThrows()
    {
        var act = () => new RelationalModelWarmupHostedService(Options.Create(new BsftDataOptions()), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task BuildsTheRelationalModelOfAContextAsItInitializes()
    {
        using var service = new RelationalModelWarmupHostedService(Options.Create(new BsftDataOptions()));
        await service.StartingAsync(CancellationToken.None);

        using var context = new FreshModelContext(Guid.NewGuid());
        var model = context.Model; // initializes the context and nothing more: no command, no query

        RelationalModelWarmup.IsBuilt(model).Should().BeTrue();
    }

    [Fact]
    public async Task HandsOverEveryContextThatInitializesWhileStarted()
    {
        var seen = new Recorder();
        using var service = new RelationalModelWarmupHostedService(Options.Create(new BsftDataOptions()), seen.Add);
        await service.StartingAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        await service.StartedAsync(CancellationToken.None);

        using var context = Initialized();

        seen.Contains(context).Should().BeTrue();
    }

    [Fact]
    public async Task ListensToNothingWhenTheOptionIsOff()
    {
        var seen = new Recorder();
        using var service = new RelationalModelWarmupHostedService(
            Options.Create(new BsftDataOptions { ShouldWarmRelationalModels = false }),
            seen.Add);
        await service.StartingAsync(CancellationToken.None);

        using var context = Initialized();

        seen.Contains(context).Should().BeFalse();
    }

    [Fact]
    public async Task StopsListeningOnceTheHostHasStopped()
    {
        var seen = new Recorder();
        using var service = new RelationalModelWarmupHostedService(Options.Create(new BsftDataOptions()), seen.Add);
        await service.StartingAsync(CancellationToken.None);
        await service.StoppingAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
        await service.StoppedAsync(CancellationToken.None);

        using var context = Initialized();

        seen.Contains(context).Should().BeFalse();
    }

    /// <summary>A subscription that arrives after disposal is released at once instead of being kept.</summary>
    [Fact]
    public async Task DoesNotStartListeningAfterItWasDisposed()
    {
        var seen = new Recorder();
        var service = new RelationalModelWarmupHostedService(Options.Create(new BsftDataOptions()), seen.Add);
        service.Dispose();

        await service.StartingAsync(CancellationToken.None);
        using var context = Initialized();

        seen.Contains(context).Should().BeFalse();
    }

    [Fact]
    public void SubscribesOnlyToEntityFrameworkCoresListenerAndOnlyForContextInitialized()
    {
        using var service = new RelationalModelWarmupHostedService(Options.Create(new BsftDataOptions()), _ => { });
        using var other = new DiagnosticListener("Bsft.Data.Tests.Other");
        using var entityFramework = new DiagnosticListener(DbLoggerCategory.Name);

        service.OnNext(other);
        service.OnNext(entityFramework);
        service.OnCompleted();
        service.OnError(new InvalidOperationException("never sent by AllListeners"));

        other.IsEnabled().Should().BeFalse();
        entityFramework.IsEnabled(CoreEventId.ContextInitialized.Name!).Should().BeTrue();
        entityFramework.IsEnabled(CoreEventId.ContextDisposed.Name!).Should().BeFalse();
    }

    [Fact]
    public void WithNullListenerThrows()
    {
        using var service = new RelationalModelWarmupHostedService(Options.Create(new BsftDataOptions()), _ => { });

        var act = () => service.OnNext(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>Creates a context and initializes it, which raises <c>ContextInitialized</c>.</summary>
    /// <returns>The initialized context.</returns>
    private static FreshModelContext Initialized()
    {
        var context = new FreshModelContext(Guid.NewGuid());
        _ = context.Model;
        return context;
    }

    /// <summary>Records the contexts it is handed, from whichever thread initialized them.</summary>
    private sealed class Recorder
    {
        private readonly object _sync = new();
        private readonly List<DbContext> _contexts = [];

        public void Add(DbContext context)
        {
            lock (_sync)
                _contexts.Add(context);
        }

        public bool Contains(DbContext context)
        {
            lock (_sync)
                return _contexts.Contains(context);
        }
    }
}
