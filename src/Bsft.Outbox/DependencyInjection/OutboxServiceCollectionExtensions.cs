using Bsft.Hosting.StartGates.DependencyInjection;
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

        if (!services.Any(descriptor => IsNameOf<TPayload>(descriptor, name)))
            services.AddSingleton(new OutboxName<TPayload>(name));

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

    /// <summary>
    /// Registers one outbox together with a hosted worker that drains it: the drain pass of
    /// <see cref="AddBsftOutbox{TPayload}(IServiceCollection, string, Action{BsftOutboxOptions}?)"/>, and
    /// a loop that drains the backlog, and waits for a wake signal or the poll
    /// interval.
    /// </summary>
    /// <typeparam name="TPayload">What the rows of this outbox carry.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">
    /// The name the options are resolved under, for the outbox and for the worker, and the key an
    /// <see cref="IOutboxWakeSignal"/> is looked up under.
    /// </param>
    /// <param name="configure">Configures the outbox bounds, or <see langword="null"/> for the defaults.</param>
    /// <param name="configureWorker">Configures the worker's own options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="name"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an outbox for <typeparamref name="TPayload"/> is already registered under another name.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The kernel stays usable without the worker: <c>AddBsftOutbox</c> registers none, and a host that
    /// runs the pass from a hosted service of its own is unaffected. A second call for the same name
    /// registers nothing further, so the worker runs once per outbox however many modules ask for it. A name
    /// maps one to one to a payload type: the drain pass is registered once per payload type, so a second
    /// name for the same type is refused with an <see cref="InvalidOperationException"/> instead of
    /// being drained under the first name's options.
    /// </para>
    /// <para>
    /// The worker wakes early on the <see cref="IOutboxWakeSignal"/> registered as a keyed service under
    /// <paramref name="name"/>, or else on one registered without a key; with neither it polls. The
    /// Postgres implementation is in <c>Bsft.Outbox.Postgres</c>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBsftOutboxDrainWorker<TPayload>(
        this IServiceCollection services,
        string name,
        Action<BsftOutboxOptions>? configure = null,
        Action<BsftOutboxDrainWorkerOptions>? configureWorker = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);

        // One drain pass exists per payload type and reads the options of the first name it was
        // registered under; a worker for a second name would drain under settings it never configured.
        if (services.Any(descriptor => IsNameOf<TPayload>(descriptor) && !IsNameOf<TPayload>(descriptor, name)))
        {
            throw new InvalidOperationException(
                $"An outbox for payload type '{typeof(TPayload).Name}' is already registered under another name, and a name maps one to one to a payload type: its drain pass is shared and bound to the first name's options. Give '{name}' a payload type of its own.");
        }

        services.AddBsftOutbox<TPayload>(name, configure);

        services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IValidateOptions<BsftOutboxDrainWorkerOptions>, BsftOutboxDrainWorkerOptionsValidator>());
        services.AddOptions<BsftOutboxDrainWorkerOptions>(name)
            .Configure(options => configureWorker?.Invoke(options))
            .ValidateOnStart();

        if (services.Any(descriptor => IsWorkerOf<TPayload>(descriptor, name)))
            return services;

        services.AddKeyedSingleton(name, (sp, key) => new OutboxDrainWorker<TPayload>(
            sp.GetRequiredService<IOutboxDrainer<TPayload>>(),
            Options.Create(sp.GetRequiredService<IOptionsMonitor<BsftOutboxOptions>>().Get(name)),
            Options.Create(sp.GetRequiredService<IOptionsMonitor<BsftOutboxDrainWorkerOptions>>().Get(name)),
            sp.GetKeyedService<IOutboxWakeSignal>(name) ?? sp.GetService<IOutboxWakeSignal>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger($"{typeof(OutboxDrainWorker<TPayload>).FullName}.{name}"),
            sp.GetService<IServiceProviderIsService>()?.IsService(typeof(IOutboxDrainPass<TPayload>)) == true
                ? sp.GetRequiredService<IServiceScopeFactory>()
                : null));

        services.AddBsftGatedHostedService(sp => sp.GetRequiredKeyedService<OutboxDrainWorker<TPayload>>(name));

        return services;
    }

    /// <summary>
    /// Registers one outbox together with its hosted drain worker, with both option sets bound from
    /// configuration before the actions are applied.
    /// </summary>
    /// <typeparam name="TPayload">What the rows of this outbox carry.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">The name the options are resolved under, and the sub-key they are bound from.</param>
    /// <param name="configuration">
    /// The configuration read from <see cref="BsftOutboxOptions.SectionName"/> and
    /// <see cref="BsftOutboxDrainWorkerOptions.SectionName"/>, each under the outbox's name.
    /// </param>
    /// <param name="configure">Applied to the outbox bounds after the section was bound, or <see langword="null"/>.</param>
    /// <param name="configureWorker">Applied to the worker options after the section was bound, or <see langword="null"/>.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/>, <paramref name="name"/> or
    /// <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddBsftOutboxDrainWorker<TPayload>(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<BsftOutboxOptions>? configure = null,
        Action<BsftOutboxDrainWorkerOptions>? configureWorker = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddBsftOutboxDrainWorker<TPayload>(
            name,
            options =>
            {
                configuration.GetSection($"{BsftOutboxOptions.SectionName}:{name}").Bind(options);
                configure?.Invoke(options);
            },
            options =>
            {
                configuration.GetSection($"{BsftOutboxDrainWorkerOptions.SectionName}:{name}").Bind(options);
                configureWorker?.Invoke(options);
            });
    }

    /// <summary>
    /// Registers the seam the hosted drain worker of <typeparamref name="TPayload"/> runs each drain pass
    /// through, as a scoped service resolved from a scope created for that pass.
    /// </summary>
    /// <typeparam name="TPayload">What the rows of the outbox carry.</typeparam>
    /// <typeparam name="TPass">The seam implementation.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// The order relative to <see cref="AddBsftOutboxDrainWorker{TPayload}(IServiceCollection, string, Action{BsftOutboxOptions}?, Action{BsftOutboxDrainWorkerOptions}?)"/>
    /// does not matter. The first registration of a payload type wins, and a worker without one drains as before.
    /// </remarks>
    public static IServiceCollection AddBsftOutboxDrainPass<TPayload, TPass>(this IServiceCollection services)
        where TPass : class, IOutboxDrainPass<TPayload>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IOutboxDrainPass<TPayload>, TPass>();

        return services;
    }

    private static bool IsNameOf<TPayload>(ServiceDescriptor descriptor, string? name = null) =>
        descriptor.ImplementationInstance is OutboxName<TPayload> registered && (name is null || registered.Value == name);

    private static bool IsWorkerOf<TPayload>(ServiceDescriptor descriptor, string name) =>
        descriptor.ServiceType == typeof(OutboxDrainWorker<TPayload>) && Equals(descriptor.ServiceKey, name);
}
