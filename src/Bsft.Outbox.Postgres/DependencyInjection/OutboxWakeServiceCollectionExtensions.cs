using Bsft.Hosting.StartGates.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Outbox.Postgres.DependencyInjection;

/// <summary>Registers the PostgreSQL wake-up of an outbox.</summary>
public static class OutboxWakeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IOutboxWakeSignal"/> of one outbox on PostgreSQL <c>LISTEN</c>, together
    /// with the hosted listener that feeds it.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">
    /// The name of the outbox, which is the name the signal is registered under as a keyed service and
    /// the name its options are resolved under. The drain worker registered under the same name picks
    /// the signal up.
    /// </param>
    /// <param name="configure">Configures the named options: the channel and the connection are required.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="name"/> is null.</exception>
    /// <remarks>
    /// Call it alongside <c>AddBsftOutboxDrainWorker</c>, never instead of it: the worker is what drains,
    /// and its poll interval stays the guarantee behind every notification that does not arrive. The
    /// writer is the host's: a trigger on the outbox table, or <c>pg_notify</c> in the transaction that
    /// wrote the row, on the channel named here. A second call for the same name registers nothing
    /// further.
    /// </remarks>
    public static IServiceCollection AddBsftOutboxPostgresWake(
        this IServiceCollection services, string name, Action<BsftOutboxPostgresOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);

        services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IValidateOptions<BsftOutboxPostgresOptions>, BsftOutboxPostgresOptionsValidator>());
        services.AddOptions<BsftOutboxPostgresOptions>(name)
            .Configure(options => configure?.Invoke(options))
            .ValidateOnStart();

        if (services.Any(descriptor => IsSignalOf(descriptor, name)))
            return services;

        services.TryAddSingleton<IPostgresListenSession, PostgresListenSession>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPostgresListenLoop, PostgresListenLoop>();

        var signal = new PostgresOutboxWakeSignal();
        services.AddKeyedSingleton<IOutboxWakeSignal>(name, signal);
        services.AddBsftGatedHostedService(sp => new PostgresOutboxWakeWorker(
            sp.GetRequiredService<IPostgresListenSession>(),
            Options.Create(sp.GetRequiredService<IOptionsMonitor<BsftOutboxPostgresOptions>>().Get(name)),
            signal,
            sp.GetRequiredService<IPostgresListenLoop>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger($"{typeof(PostgresOutboxWakeWorker).FullName}.{name}")));

        return services;
    }

    /// <summary>
    /// Registers the wake-up of one outbox with its options bound from configuration before
    /// <paramref name="configure"/> is applied.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">The name of the outbox, and the sub-key its options are bound from.</param>
    /// <param name="configuration">The configuration read from <see cref="BsftOutboxPostgresOptions.SectionName"/>, under the outbox's name.</param>
    /// <param name="configure">Applied after the section was bound, or <see langword="null"/>.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/>, <paramref name="name"/> or <paramref name="configuration"/> is null.</exception>
    public static IServiceCollection AddBsftOutboxPostgresWake(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<BsftOutboxPostgresOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddBsftOutboxPostgresWake(name, options =>
        {
            configuration.GetSection($"{BsftOutboxPostgresOptions.SectionName}:{name}").Bind(options);
            configure?.Invoke(options);
        });
    }

    private static bool IsSignalOf(ServiceDescriptor descriptor, string name) =>
        descriptor.ServiceType == typeof(IOutboxWakeSignal) && Equals(descriptor.ServiceKey, name);
}
