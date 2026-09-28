using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Outbox.DependencyInjection;

/// <summary>Registers the outbox drain kernel and one outbox on top of it.</summary>
public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers the drain pass for one outbox, together with the retry schedule every outbox in the
    /// host shares.
    /// </summary>
    /// <typeparam name="TPayload">What the rows of this outbox carry.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">
    /// The name the drain options are resolved under. A second outbox is a second name rather than a
    /// second configuration shape, which is what keeps two outboxes in one host from needing two
    /// spellings of the same six settings.
    /// </param>
    /// <param name="configure">Configures the named options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="name"/> is null.
    /// </exception>
    /// <remarks>
    /// The store and the side effect are not registered here: which rows this outbox holds and what
    /// they mean are the two things only the owning package knows, and a registration that guessed
    /// either would be a registration a host has to undo.
    /// </remarks>
    public static IServiceCollection AddBsftOutbox<TPayload>(
        this IServiceCollection services, string name, Action<BsftOutboxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);

        services.TryAddSingleton<IOutboxRetrySchedule>(_ => new OutboxRetrySchedule(Random.Shared));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftOutboxOptions>, BsftOutboxOptionsValidator>());

        services.AddOptions<BsftOutboxOptions>(name)
            .Configure(options => configure?.Invoke(options))
            .ValidateOnStart();

        services.TryAddSingleton<IOutboxDrainer<TPayload>>(sp => new OutboxDrainer<TPayload>(
            sp.GetRequiredService<IOutboxStore<TPayload>>(),
            sp.GetRequiredService<IOutboxSideEffect<TPayload>>(),
            sp.GetRequiredService<IOutboxRetrySchedule>(),
            sp.GetRequiredService<IOptionsMonitor<BsftOutboxOptions>>().Get(name),
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(OutboxDrainer<TPayload>))));

        return services;
    }

    /// <summary>
    /// Registers the drain pass for one outbox, with its options bound from configuration before
    /// <paramref name="configure"/> is applied.
    /// </summary>
    /// <typeparam name="TPayload">What the rows of this outbox carry.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">The name the drain options are resolved under, and the sub-key they are bound from.</param>
    /// <param name="configuration">The configuration the section is read from.</param>
    /// <param name="configure">Applied after the section has been bound, or <see langword="null"/>.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/>, <paramref name="name"/> or
    /// <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddBsftOutbox<TPayload>(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<BsftOutboxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddBsftOutbox<TPayload>(name, options =>
        {
            configuration.GetSection($"{BsftOutboxOptions.SectionName}:{name}").Bind(options);
            configure?.Invoke(options);
        });
    }
}
