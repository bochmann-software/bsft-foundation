using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Execution;

public class SagaStepScopeFactoryTests
{
    [Fact]
    public async Task CreateAsync_WhenCorrelationIsAmbient_HandsItToTheStepScope()
    {
        using var provider = BuildProvider();
        var ambient = provider.GetRequiredService<ISagaCorrelationScope>();
        var factory = provider.GetRequiredService<ISagaStepScopeFactory>();

        using (ambient.Use("from-caller"))
        {
            await using var scope = await factory.CreateAsync();

            scope.ServiceProvider.GetRequiredService<RecordingInitializer>()
                .Restored.Should().Be("from-caller");
        }
    }

    [Fact]
    public async Task CreateAsync_WithNothingAmbient_LeavesTheStepScopeAlone()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<ISagaStepScopeFactory>();

        await using var scope = await factory.CreateAsync();

        // The scope's own correlation is the right answer here, so the initializer is never called.
        scope.ServiceProvider.GetRequiredService<RecordingInitializer>().Restored.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WithoutAnInitializerRegistered_StillCreatesAScope()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();

        using var provider = services.BuildServiceProvider();
        var ambient = provider.GetRequiredService<ISagaCorrelationScope>();
        var factory = provider.GetRequiredService<ISagaStepScopeFactory>();

        using (ambient.Use("from-caller"))
        {
            var act = async () => await (await factory.CreateAsync()).DisposeAsync();

            // The saga engine has no correlation of its own; the seam is simply unused.
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task CreateAsync_WhenInitializerFails_DisposesTheScopeAsynchronously()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        services.AddScoped<AsyncOnlyDisposable>();
        services.AddScoped<ThrowingInitializer>();
        services.AddScoped<ISagaCorrelationInitializer>(sp => sp.GetRequiredService<ThrowingInitializer>());

        using var provider = services.BuildServiceProvider();
        var ambient = provider.GetRequiredService<ISagaCorrelationScope>();
        var factory = provider.GetRequiredService<ISagaStepScopeFactory>();

        AsyncOnlyDisposable.Reset();

        using (ambient.Use("from-caller"))
        {
            var act = async () => await factory.CreateAsync();

            // The ORIGINAL initializer failure propagates. A synchronous Dispose would instead throw
            // its own InvalidOperationException about the async-only-disposable service and mask this.
            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("The host's initializer failed.");
        }

        // The initializer pulled an async-only-disposable service into the scope before it threw, so a
        // clean run is proof the factory tore the scope down through DisposeAsync, not a sync Dispose.
        AsyncOnlyDisposable.DisposedAsync.Should().BeTrue();
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        services.AddScoped<RecordingInitializer>();
        services.AddScoped<ISagaCorrelationInitializer>(sp => sp.GetRequiredService<RecordingInitializer>());

        return services.BuildServiceProvider();
    }

    private sealed class RecordingInitializer : ISagaCorrelationInitializer
    {
        public string? Restored { get; private set; }

        public void Restore(string correlationId) => Restored = correlationId;
    }

    /// <summary>
    /// Scoped service that only implements <see cref="IAsyncDisposable"/>. Disposing the scope it lives
    /// in synchronously would throw; a recorded asynchronous disposal proves the scope was awaited.
    /// </summary>
    private sealed class AsyncOnlyDisposable : IAsyncDisposable
    {
        /// <summary>Whether the most recently built instance was disposed asynchronously.</summary>
        public static bool DisposedAsync { get; private set; }

        public static void Reset() => DisposedAsync = false;

        public ValueTask DisposeAsync()
        {
            DisposedAsync = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingInitializer : ISagaCorrelationInitializer
    {
        // Depending on the async-only-disposable service pulls it into the step scope before Restore
        // throws, so the scope holds a service that a synchronous Dispose could not tear down.
        public ThrowingInitializer(AsyncOnlyDisposable asyncOnly)
        {
            if (asyncOnly is null)
                throw new ArgumentNullException(nameof(asyncOnly));
        }

        public void Restore(string correlationId) =>
            throw new InvalidOperationException("The host's initializer failed.");
    }
}
