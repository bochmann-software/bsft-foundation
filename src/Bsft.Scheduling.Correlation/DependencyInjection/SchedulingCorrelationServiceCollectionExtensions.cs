using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Scheduling.Correlation.DependencyInjection;

/// <summary>
/// Dependency injection extensions that bridge <c>Bsft.Correlation</c> and the job scheduler.
/// </summary>
public static class SchedulingCorrelationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the correlation accessor used to stamp a correlation id onto an enqueued job and to
    /// restore it when the job executes.
    /// </summary>
    /// <remarks>
    /// Registered with a plain <c>Add</c> rather than <c>TryAdd</c> so that it replaces the ambient-only
    /// default from <c>AddBsftScheduling</c> whichever order the two are called in.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftSchedulingCorrelation(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.AddScoped<IJobCorrelationAccessor, CorrelationContextJobAccessor>();
        return services;
    }
}
