using Bsft.Hosting.BackgroundWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering background-work services.
/// </summary>
public static class BackgroundWorkServiceCollectionExtensions
{
    /// <summary>
    /// Registers a run-once startup operation as a hosted service, together with its named
    /// <see cref="BsftRunOnceOptions"/>.
    /// </summary>
    /// <typeparam name="TService">The run-once background service.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">An optional action to configure this service's options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// The options are named after <typeparamref name="TService"/>, so several startup operations
    /// coexist with separate backoff schedules, and they are validated by <c>ValidateOnStart()</c> so
    /// a schedule that cannot be waited fails the host rather than the first backoff.
    /// </remarks>
    public static IServiceCollection AddBsftBackgroundWorkRunOnce<TService>(
        this IServiceCollection services,
        Action<BsftRunOnceOptions>? configure = null)
        where TService : RunOnceWithRetryBackgroundServiceBase
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add<TService>(services, configure);
    }

    /// <summary>
    /// Registers a run-once startup operation with its options bound from configuration section
    /// <see cref="BsftRunOnceOptions.SectionName"/> followed by the service type's full name.
    /// </summary>
    /// <typeparam name="TService">The run-once background service.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration the section is read from.</param>
    /// <param name="configure">An optional action applied after the section has been bound.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <remarks>
    /// The per-service section keeps several startup operations apart in one file, so a host writes
    /// <c>Bsft:Hosting:RunOnce:MyApp.Startup.RegisterInstanceService</c>. The section is bound first
    /// and the delegate runs after it, so code wins over configuration for anything both set.
    /// </remarks>
    public static IServiceCollection AddBsftBackgroundWorkRunOnce<TService>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftRunOnceOptions>? configure = null)
        where TService : RunOnceWithRetryBackgroundServiceBase
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = $"{BsftRunOnceOptions.SectionName}:{BsftRunOnceOptions.NameFor<TService>()}";

        return Add<TService>(services, options =>
        {
            configuration.GetSection(section).Bind(options);
            configure?.Invoke(options);
        });
    }

    /// <summary>Registers everything both overloads register.</summary>
    /// <typeparam name="TService">The run-once background service.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">What shapes the options, whatever it was built from.</param>
    /// <returns>The service collection for chaining.</returns>
    private static IServiceCollection Add<TService>(IServiceCollection services, Action<BsftRunOnceOptions>? configure)
        where TService : RunOnceWithRetryBackgroundServiceBase
    {
        services.AddOptions<BsftRunOnceOptions>(BsftRunOnceOptions.NameFor<TService>())
            .Configure(options => configure?.Invoke(options))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftRunOnceOptions>, BsftRunOnceOptionsValidator>());

        // The base class needs both even in a container that was given nothing else.
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);

        services.AddHostedService<TService>();

        return services;
    }
}
