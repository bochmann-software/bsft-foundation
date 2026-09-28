using Bsft.EventAggregator.Saga.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.EventAggregator.Saga.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IServiceProvider"/> for starting saga flows.
/// </summary>
public static class SagaServiceProviderExtensions
{
    /// <summary>
    /// Resolves the <see cref="SagaFlowHost"/> and starts it, configuring all registered saga flows
    /// and subscribing to their trigger events.
    /// </summary>
    /// <param name="services">The service provider to resolve from.</param>
    /// <returns>The service provider for chaining.</returns>
    public static IServiceProvider UseBsftSagaFlows(this IServiceProvider services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        var host = services.GetRequiredService<SagaFlowHost>();
        host.Start();
        return services;
    }
}
