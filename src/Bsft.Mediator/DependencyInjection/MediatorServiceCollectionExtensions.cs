using Bsft.Core;
using Bsft.Instrumentation.DependencyInjection;
using Bsft.Mediator.StartupValidation;
using Bsft.Mediator.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Mediator.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to register BSFT mediator services.
/// </summary>
public static class MediatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers the BSFT mediator, its handlers, and pipeline behaviors with the service collection.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="configure">An action to configure the mediator by registering handlers and behaviors.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddBsftMediator(
        this IServiceCollection services,
        Action<MediatorConfiguration> configure)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (configure is null)
            throw new ArgumentNullException(nameof(configure));

        var configuration = new MediatorConfiguration();
        configure(configuration);

        configuration.RegisterHandlers(services);
        configuration.RegisterBehaviors(services);

        // Telemetry is always available so the dispatcher can instrument notifications; emission is a
        // no-op until an observability backend attaches a listener/exporter. The "Bsft.Mediator"
        // source is advertised for discovery via the vendor-neutral instrumentation layer.
        services.AddBsftInstrumentationSource(MediatorTelemetry.SourceName);
        // TryAdd, because the emitter is the documented seam for sending somewhere other than the
        // shipped ActivitySource: a host that registers its own would otherwise be resolved past.
        services.TryAddSingleton<IMediatorTelemetry, MediatorTelemetry>();

        services.AddSingleton(configuration);

        // The live collection, so the startup validator sees the behaviors registered after this
        // call too — a behavior package's own AddBsft* or a direct registration — and can report one
        // that ended up in the pipeline twice.
        services.AddSingleton(new PipelineBehaviorRegistrations(services));

        // One reflection pass over the configured assemblies, shared by every startup check, so the
        // assemblies and their interfaces are walked once per application start.
        services.AddSingleton<MediatorTypeScanner>();

        // TryAdd for the same reason, so a host that replaces the dispatcher keeps its own
        // regardless of whether it registered before or after this call.
        services.TryAddScoped<IMediator, DefaultMediator>();

        // The seam an attribute-driven behavior reads the concrete request type through. A singleton
        // because the descriptor it caches is fixed once the assemblies are loaded, and TryAdd
        // because a host that supplies its own provider keeps it — this is a cache, not a policy.
        services.TryAddSingleton<IRequestMetadataProvider, RequestMetadataProvider>();

        // Both validators are built by hand, because the scan they share is an internal type the
        // container cannot inject into a public constructor.
        services.AddTransient<IStartupValidator>(provider => new MediatorStartupValidator(
            configuration,
            provider,
            provider.GetRequiredService<MediatorTypeScanner>()));

        // Independent of UseDefaults(): a request declaring a permission that nothing enforces is a
        // gap in a hand-wired pipeline just as much as in the default chain.
        services.AddTransient<IStartupValidator>(provider => new AuthorizationCoverageValidator(
            configuration,
            provider.GetRequiredService<MediatorTypeScanner>(),
            provider.GetService<PipelineBehaviorRegistrations>()));

        // The resolver is shared by the transaction behavior and the startup check, so its cache and
        // its ambiguity rule live in one place; a singleton because both read registrations that do
        // not change after startup.
        services.AddSingleton<Transactions.ITransactionDbContextResolver, Transactions.TransactionDbContextResolver>();

        // Reads the live collection, so a DbContext registered after this call is seen; the check
        // only fires when the transaction behavior is actually in the pipeline. It reuses the shared
        // resolver's ambiguity rule rather than restating it.
        services.AddTransient<IStartupValidator>(provider => new TransactionDbContextValidator(
            services,
            provider.GetRequiredService<PipelineBehaviorRegistrations>(),
            provider.GetRequiredService<Transactions.ITransactionDbContextResolver>()));

        return services;
    }
}
