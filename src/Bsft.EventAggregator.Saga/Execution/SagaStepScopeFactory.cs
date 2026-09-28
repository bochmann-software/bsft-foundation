using Bsft.EventAggregator.Saga.Timeouts;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.EventAggregator.Saga.Execution;

/// <summary>
/// Creates the service scope a saga step runs in, already carrying the correlation of whatever
/// caused the step.
/// </summary>
/// <remarks>
/// The scope is created from the root provider, which is what makes a step independent of the
/// request that triggered it. The cost is that every scoped service in it starts empty, so a
/// correlation context that mints an id per scope would start a new correlation on every hop. The
/// ambient id — restored by a firing timeout, or by the request that published the event — is
/// therefore handed to the new scope through <see cref="ISagaCorrelationInitializer"/>.
/// </remarks>
internal sealed class SagaStepScopeFactory : ISagaStepScopeFactory
{
    private readonly IServiceProvider _services;
    private readonly ISagaCorrelationScope? _correlationScope;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaStepScopeFactory"/> class.
    /// </summary>
    /// <param name="services">The provider that step scopes are created from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public SagaStepScopeFactory(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        // Optional: a host that carries no correlation at all resolves nothing here, and every step
        // simply keeps the id its own scope mints.
        _correlationScope = services.GetService<ISagaCorrelationScope>();
    }

    /// <inheritdoc />
    public async ValueTask<AsyncServiceScope> CreateAsync(CancellationToken cancellationToken = default)
    {
        // An async scope so the caller can await its disposal: a step resolves scoped services that
        // are async-disposable (an EF Core DbContext among them), and only DisposeAsync tears those
        // down deterministically once the awaited step has finished.
        var scope = _services.CreateAsyncScope();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_correlationScope?.Current is { } correlationId)
                scope.ServiceProvider.GetService<ISagaCorrelationInitializer>()?.Restore(correlationId);
        }
        catch
        {
            // The scope has not reached the caller yet, so nobody else can dispose it. A host's own
            // initializer failing must not also leak the scope and everything resolved in it. Disposal
            // is awaited so an async-only-disposable scoped service resolved before the failure — the
            // initializer may pull one in — is torn down through DisposeAsync; a synchronous Dispose
            // would throw its own InvalidOperationException and mask the original failure.
            await scope.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return scope;
    }
}
