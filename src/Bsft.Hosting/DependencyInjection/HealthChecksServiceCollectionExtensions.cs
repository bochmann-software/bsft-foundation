using Bsft.Hosting.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering BSFT health check services.
/// </summary>
public static class HealthChecksServiceCollectionExtensions
{
    /// <summary>
    /// Adds BSFT health check services with optional custom health check registrations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftHealthCheckConfiguration"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBsftHealthChecks(
        this IServiceCollection services,
        Action<BsftHealthCheckConfiguration>? configure = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        var config = new BsftHealthCheckConfiguration();
        configure?.Invoke(config);

        var builder = services.AddHealthChecks();

        if (config.ExternalServices.Count > 0)
        {
            // The check resolves IHttpClientFactory, and nothing else here registers it. Leaving that
            // to the host meant the gap surfaced only when the check first ran, as a failing readiness
            // probe in a deployed service rather than as anything a build or a startup would catch.
            services.AddHttpClient();
        }

        foreach (var externalService in config.ExternalServices)
        {
            var registration = externalService;
            builder.Add(new HealthCheckRegistration(
                $"external-{registration.Name}",
                sp =>
                {
                    var factory = sp.GetRequiredService<IHttpClientFactory>();
                    return new ExternalServiceHealthCheck(registration.Name, registration.HealthUrl, factory);
                },
                failureStatus: null,
                tags: ["ready"]));
        }

        foreach (var diskCheck in config.DiskStorageChecks)
        {
            var registration = diskCheck;
            builder.Add(new HealthCheckRegistration(
                $"disk-storage-{registration.MinFreeMb}mb",
                _ => new DiskStorageHealthCheck(registration.MinFreeMb),
                failureStatus: null,
                tags: ["ready"]));
        }

        return services;
    }
}
