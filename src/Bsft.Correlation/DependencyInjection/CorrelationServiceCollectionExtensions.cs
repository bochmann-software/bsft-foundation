using Bsft.Correlation.Http.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Correlation.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to register BSFT correlation services.
/// </summary>
public static class CorrelationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the BSFT correlation ID infrastructure: the scoped correlation context, and the
    /// outbound delegating handler of <c>Bsft.Correlation.Http</c>.
    /// </summary>
    /// <remarks>
    /// The handler lives in the client-safe <c>Bsft.Correlation.Http</c> package, which this one
    /// references so that a host still gets both halves of correlation from a single call.
    /// </remarks>
    /// <param name="services">The service collection to register services with.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddBsftCorrelation(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());
        services.AddBsftCorrelationHttp();

        return services;
    }
}
