using Bsft.EventAggregator.Saga.Timeouts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.EventAggregator.Saga.Correlation.DependencyInjection;

/// <summary>
/// Dependency injection extensions that bridge <c>Bsft.Correlation</c> and saga timeouts.
/// </summary>
public static class SagaCorrelationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the correlation accessor used to stamp a correlation id onto a scheduled timeout and
    /// to restore it when the timeout fires, and the initializer that hands the id a step is running
    /// under to that step's own correlation context.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// Add <c>UseBsftSagaCorrelation()</c> to the request pipeline as well to close the chain for a step
    /// triggered directly by an event a request publishes; the timeout path needs no pipeline step.
    /// </remarks>
    public static IServiceCollection AddBsftSagaCorrelation(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        // The accessor reads the ambient id of a firing timeout, so the scope has to be resolvable even
        // when only this bridge is registered. TryAdd keeps the saga engine's own registration.
        services.TryAddSingleton<ISagaCorrelationScope, SagaCorrelationScope>();
        services.TryAddScoped<ISagaCorrelationAccessor, CorrelationContextSagaAccessor>();
        services.TryAddScoped<ISagaCorrelationInitializer, SagaCorrelationInitializer>();
        return services;
    }
}
