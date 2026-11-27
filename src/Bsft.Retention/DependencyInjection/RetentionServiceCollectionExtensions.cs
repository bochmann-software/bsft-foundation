using Bsft.Hosting.StartGates.DependencyInjection;
using Bsft.Retention.Watermarks;
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

    /// <summary>
    /// Keeps the archive <typeparamref name="TSource"/> lists under a disk watermark: once it grows past
    /// <see cref="DiskWatermarkOptions.MaxArchiveBytes"/> or its volume keeps less than
    /// <see cref="DiskWatermarkOptions.MinAvailableBytes"/>, the oldest items are deleted until both
    /// hold again with the room <see cref="DiskWatermarkOptions.TargetFillRatio"/> leaves.
    /// </summary>
    /// <typeparam name="TSource">The archive, which lists its items oldest first and deletes one.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">An optional action to configure the watermark.</param>
    /// <param name="configureRetention">
    /// An optional action to configure the sweep. Its <see cref="BsftRetentionOptions.RetentionPeriod"/>
    /// is the minimum age before an item may be deleted for space, and leaving it unset runs no sweep.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// The watermark is an ordinary retention pass, registered through <see cref="AddBsftRetention{TPass}(IServiceCollection, Action{BsftRetentionOptions}?)"/>
    /// and swept by its own <see cref="RetentionSweeper{TPass}"/>; both options are named
    /// <see cref="DiskWatermarkOptions.NameFor{TSource}"/>. <typeparamref name="TSource"/> is registered
    /// as scoped, and the volume is measured by <see cref="DriveInfo"/> unless an
    /// <see cref="IVolumeSpaceProvider"/> is registered first.
    /// </remarks>
    public static IServiceCollection AddBsftRetentionDiskWatermark<TSource>(
        this IServiceCollection services,
        Action<DiskWatermarkOptions>? configure = null,
        Action<BsftRetentionOptions>? configureRetention = null)
        where TSource : class, IDiskWatermarkSource
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        return AddDiskWatermarkCore<TSource>(services, configure, configureRetention);
    }

    /// <summary>
    /// Keeps an archive under a disk watermark with both its options bound from configuration section
    /// <see cref="DiskWatermarkOptions.SectionName"/> followed by <see cref="DiskWatermarkOptions.NameFor{TSource}"/>.
    /// </summary>
    /// <typeparam name="TSource">The archive, which lists its items oldest first and deletes one.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration the section is read from.</param>
    /// <param name="configure">An optional action applied to the watermark after the section has been bound.</param>
    /// <param name="configureRetention">An optional action applied to the sweep after the section has been bound.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <remarks>
    /// One section carries both: <c>RetentionPeriod</c>, <c>SweepInterval</c> and the other sweep
    /// settings beside <c>MaxArchiveBytes</c>, <c>MinAvailableBytes</c>, <c>VolumePath</c> and
    /// <c>TargetFillRatio</c>. Code wins over configuration for anything both set.
    /// </remarks>
    public static IServiceCollection AddBsftRetentionDiskWatermark<TSource>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DiskWatermarkOptions>? configure = null,
        Action<BsftRetentionOptions>? configureRetention = null)
        where TSource : class, IDiskWatermarkSource
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        var section = configuration.GetSection($"{DiskWatermarkOptions.SectionName}:{DiskWatermarkOptions.NameFor<TSource>()}");

        return AddDiskWatermarkCore<TSource>(
            services,
            options =>
            {
                section.Bind(options);
                configure?.Invoke(options);
            },
            options =>
            {
                section.Bind(options);
                configureRetention?.Invoke(options);
            });
    }

    /// <summary>Registers everything both disk watermark overloads register.</summary>
    /// <typeparam name="TSource">The archive kept under the watermark.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">What shapes the watermark options, whatever it was built from.</param>
    /// <param name="configureRetention">What shapes the sweep options, whatever it was built from.</param>
    /// <returns>The service collection for chaining.</returns>
    private static IServiceCollection AddDiskWatermarkCore<TSource>(
        IServiceCollection services,
        Action<DiskWatermarkOptions>? configure,
        Action<BsftRetentionOptions>? configureRetention)
        where TSource : class, IDiskWatermarkSource
    {
        services.AddOptions<DiskWatermarkOptions>(DiskWatermarkOptions.NameFor<TSource>())
            .Configure(options => configure?.Invoke(options))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<DiskWatermarkOptions>, DiskWatermarkOptionsValidator>());

        // Scoped like the pass, so the source may take a DbContext.
        services.TryAddScoped<TSource>();
        services.TryAddSingleton<IVolumeSpaceProvider, VolumeSpaceProvider>();
        services.TryAddSingleton<IDiskWatermarkPlanner, DiskWatermarkPlanner>();

        // One store for every watermark pass of the host; the reader is the same instance, so what the
        // pass writes is what a reader sees.
        services.TryAddSingleton<IDiskWatermarkStatusStore, DiskWatermarkStatusStore>();
        services.TryAddSingleton<IDiskWatermarkStatusReader>(
            serviceProvider => serviceProvider.GetRequiredService<IDiskWatermarkStatusStore>());

        var registered = AddCore<DiskWatermarkRetentionPass<TSource>>(services, configureRetention);

        // The pass reads the sweep's cutoff as "nothing created at or after it is evicted", which is
        // what makes RetentionPeriod the minimum age. Absolute-expiry mode hands it the current time
        // instead, which would evict the newest items and drop that protection, so it is refused at
        // start rather than ignored.
        var passName = BsftRetentionOptions.NameFor<DiskWatermarkRetentionPass<TSource>>();
        services.AddOptions<BsftRetentionOptions>(passName)
            .Validate(
                options => !options.UseAbsoluteExpiry,
                $"{passName}: {nameof(BsftRetentionOptions.UseAbsoluteExpiry)} is not valid for a disk watermark pass, "
                + $"because it would evict items regardless of their age; leave it off and set {nameof(BsftRetentionOptions.RetentionPeriod)} as the minimum age.");

        return registered;
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

        services.AddBsftGatedHostedService<RetentionSweeper<TPass>>();

        return services;
    }
}
