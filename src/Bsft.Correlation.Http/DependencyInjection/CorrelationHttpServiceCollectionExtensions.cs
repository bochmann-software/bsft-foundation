using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Correlation.Http.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to register the outbound half of BSFT
/// correlation: the delegating handler that carries the correlation ID to the next service.
/// </summary>
public static class CorrelationHttpServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CorrelationIdDelegatingHandler"/> as a transient service, so that a named
    /// or typed client can take it through <c>AddHttpMessageHandler&lt;CorrelationIdDelegatingHandler&gt;()</c>.
    /// </summary>
    /// <remarks>
    /// The handler resolves <see cref="Bsft.Correlation.ICorrelationContext"/>, which this package does not
    /// register: a host running the request pipeline gets it from <c>AddBsftCorrelation()</c>, and a
    /// client without one registers its own implementation. Registration is idempotent, so calling this
    /// beside <c>AddBsftCorrelation()</c> leaves a single handler registration.
    /// </remarks>
    /// <param name="services">The service collection to register services with.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddBsftCorrelationHttp(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddTransient<CorrelationIdDelegatingHandler>();

        return services;
    }
}
