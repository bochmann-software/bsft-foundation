using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Retention.DependencyInjection;

/// <summary>
/// Extension methods for registering a retention pass and the sweeper that drives it.
/// </summary>
public static class RetentionServiceCollectionExtensions
{
    /// <summary>
    /// Registers a retention pass and the sweeper that drives it: the loop, the retention window, the
    /// batching, the pacing and the deterministic startup offset are the framework's, and
    /// <typeparamref name="TPass"/> supplies only the delete.
    /// </summary>
    /// <typeparam name="TPass">The retention pass implementation.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">An optional action to configure this pass's <see cref="BsftRetentionOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// A sweep is its own hosted loop and needs neither a job store nor a dispatcher, so a host may
    /// purge without running a scheduler at all. The options are named after <typeparamref name="TPass"/>,
    /// so several passes coexist with separate windows and batch sizes, and nothing sweeps until
    /// <see cref="BsftRetentionOptions.RetentionPeriod"/> is set.
    /// </remarks>
    public static IServiceCollection AddBsftRetention<TPass>(
        this IServiceCollection services,
        Action<BsftRetentionOptions>? configure = null)
        where TPass : class, IRetentionPass
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        return AddCore<TPass>(services, configure);
    }

    /// <summary>
    /// Registers a retention pass with its options bound from configuration section
    /// <see cref="BsftRetentionOptions.SectionName"/> followed by the pass type's full name.
    /// </summary>
    /// <typeparam name="TPass">The retention pass implementation.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration the section is read from.</param>
    /// <param name="configure">An optional action applied after the section has been bound.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <remarks>
    /// The per-pass section keeps several passes apart in one file, so a host writes
    /// <c>Bsft:Retention:MyApp.Auditing.AuditLogRetentionPass</c>. The section is bound first and the
    /// delegate runs after it, so code wins over configuration for anything both set.
    /// </remarks>
    public static IServiceCollection AddBsftRetention<TPass>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftRetentionOptions>? configure = null)
        where TPass : class, IRetentionPass
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        var section = $"{BsftRetentionOptions.SectionName}:{BsftRetentionOptions.NameFor<TPass>()}";

        return AddCore<TPass>(services, options =>
        {
            configuration.GetSection(section).Bind(options);
            configure?.Invoke(options);
        });
    }

    /// <summary>Registers everything both overloads register.</summary>
    /// <typeparam name="TPass">The retention pass implementation.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">What shapes the options, whatever it was built from.</param>
    /// <returns>The service collection for chaining.</returns>
    private static IServiceCollection AddCore<TPass>(
        IServiceCollection services,
        Action<BsftRetentionOptions>? configure)
        where TPass : class, IRetentionPass
    {
        var passName = BsftRetentionOptions.NameFor<TPass>();

        services.AddOptions<BsftRetentionOptions>(passName)
            .Configure(options => configure?.Invoke(options))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftRetentionOptions>, BsftRetentionOptionsValidator>());

        // Scoped, so a pass may take a DbContext; the sweeper creates a fresh scope for every sweep.
        services.TryAddScoped<TPass>();

        // The sweeper needs both even where nothing else in the host registered them.
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);

        services.AddHostedService<RetentionSweeper<TPass>>();

        return services;
    }
}
