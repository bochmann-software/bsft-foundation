using Bsft.Calendar.DependencyInjection;
using Bsft.EventAggregator.Saga.Diagnostics;
using Bsft.EventAggregator.Saga.Execution;
using Bsft.EventAggregator.Saga.Persistence;
using Bsft.EventAggregator.Saga.Timeouts;
using Bsft.Instrumentation.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.EventAggregator.Saga.DependencyInjection;

/// <summary>
/// Extension methods for registering saga engine services with the dependency injection container.
/// </summary>
public static class SagaServiceCollectionExtensions
{
    /// <summary>
    /// Registers the saga engine, the in-memory saga and timeout stores, the builder factory and the
    /// flow host.
    /// </summary>
    /// <remarks>
    /// Every store is registered with <c>TryAdd</c>, so a persistence package registered afterwards —
    /// <c>AddBsftSagaEFCore&lt;TContext&gt;()</c> — replaces them, and an application that registers its
    /// own beforehand keeps it.
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftSagaOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftSaga(
        this IServiceCollection services,
        Action<BsftSagaOptions>? configure = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.AddOptions<BsftSagaOptions>()
            .Configure(options => configure?.Invoke(options))
            .ValidateOnStart();

        return AddCore(services);
    }

    /// <summary>
    /// Registers the saga engine with its options bound from configuration section
    /// <see cref="BsftSagaOptions.SectionName"/>.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration to bind <c>Bsft:Saga</c> from.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static IServiceCollection AddBsftSaga(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        var section = configuration.GetSection(BsftSagaOptions.SectionName);

        services.AddOptions<BsftSagaOptions>()
            .Configure(options => section.Bind(options))
            .ValidateOnStart();

        return AddCore(services);
    }

    /// <summary>
    /// Registers a saga flow implementation as a transient service.
    /// </summary>
    /// <typeparam name="T">The saga flow type to register.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftSagaFlow<T>(this IServiceCollection services)
        where T : class, ISagaFlow
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.AddTransient<ISagaFlow, T>();
        return services;
    }

    /// <summary>Registers everything the two entry-point overloads share.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    private static IServiceCollection AddCore(IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftSagaOptions>, BsftSagaOptionsValidator>());

        services.TryAddSingleton<ISagaStore, InMemorySagaStore>();
        services.TryAddSingleton<ISagaTimeoutStore, InMemorySagaTimeoutStore>();

        // Stores that make every write durable on its own need no shared window. A provider that can
        // commit the state advance and the timeout writes together registers its own, and TryAdd
        // leaves that registration in place.
        services.TryAddSingleton<ISagaPersistenceWindow, ImmediateSagaPersistenceWindow>();

        // The timeout scheduler and dispatcher both report failures, and the scheduler is resolved
        // eagerly by the flow host, so logging has to be available even in a bare container.
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddBsftCalendar();
        services.AddSingleton<ISagaTimeoutScheduler, SagaTimeoutScheduler>();

        // Singleton, not scoped: the dispatcher writes the ambient correlation id and the readers run
        // in the step's own service scope, so they have to share one instance for the value to flow.
        services.TryAddSingleton<ISagaCorrelationScope, SagaCorrelationScope>();
        services.TryAddSingleton<ISagaTimeoutPublisher, SagaTimeoutPublisher>();

        // One dispatcher behind both roles: the host polls it as a background service, and an
        // on-demand drain goes through the same instance, so a test flushes the timers the
        // application would have fired rather than a second dispatcher over the same stores.
        services.AddSingleton<SagaTimeoutDispatcher>();
        services.AddSingleton<ISagaTimeoutDrainer>(sp => sp.GetRequiredService<SagaTimeoutDispatcher>());
        services.AddHostedService(sp => sp.GetRequiredService<SagaTimeoutDispatcher>());

        // Telemetry emits a no-op until an observability backend attaches a listener/exporter; the
        // "Bsft.Saga" source is advertised for discovery via the vendor-neutral instrumentation layer.
        services.AddBsftInstrumentationSource(SagaTelemetry.SourceName);
        services.AddSingleton<ISagaTelemetry, SagaTelemetry>();

        services.AddSingleton<ISagaStepScopeFactory, SagaStepScopeFactory>();
        services.AddSingleton<ISagaInstanceRegistry, SagaInstanceRegistry>();

        // One commit path for both entry points into a saga step — the event-driven flow host and the
        // direct StartSagaAsync API — so neither can advance a saga while losing its timers.
        services.AddSingleton<ISagaStepCommitter, SagaStepCommitter>();
        services.AddSingleton<ISagaEngine, SagaEngine>();
        services.AddSingleton<ISagaBuilderFactory, SagaBuilderFactory>();
        services.AddSingleton(provider => new SagaFlowHost(
            provider.GetServices<ISagaFlow>(),
            provider.GetRequiredService<ISagaEngine>(),
            provider.GetRequiredService<ISagaBuilderFactory>(),
            provider.GetRequiredService<ISagaTelemetry>(),
            provider.GetRequiredService<ISagaStepCommitter>(),
            provider.GetRequiredService<ISagaInstanceRegistry>(),
            provider.GetRequiredService<ISagaStepScopeFactory>()));

        return services;
    }
}
