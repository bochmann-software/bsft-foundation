using Bsft.Idempotency.Auditing;
using Bsft.Idempotency.Diagnostics;
using Bsft.Idempotency.Fingerprinting;
using Bsft.Idempotency.Replay;
using Bsft.Instrumentation;
using Bsft.Instrumentation.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Idempotency.DependencyInjection;

/// <summary>
/// Registers idempotent-request handling: the fingerprinter, the record store, the decision gate and
/// the audit and telemetry seams.
/// </summary>
public static class IdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Adds idempotent-request handling with an in-process record store.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">An optional action configuring the header, methods and windows.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// The pipeline half is a second call: <c>UseBsftIdempotency()</c>, after
    /// <c>UseBsftDefaults()</c> and after <c>UseRouting()</c>. The default store remembers keys in
    /// one process, which is enough for a single instance and is not enough for two —
    /// <c>AddBsftIdempotencyEFCore&lt;TContext&gt;</c> replaces it with a shared table.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the configured options cannot be used.</exception>
    public static IServiceCollection AddBsftIdempotency(
        this IServiceCollection services, Action<BsftIdempotencyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configure ?? (_ => { }));
    }

    /// <summary>
    /// Adds idempotent-request handling with its options bound from configuration section
    /// <see cref="BsftIdempotencyOptions.SectionName"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">The configuration the section is read from.</param>
    /// <param name="configure">An optional action applied after the binding, so code has the last word.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// A list the section names replaces the default rather than extending it, so
    /// <c>"Methods": [ "POST", "PATCH" ]</c> means those two and not <c>POST</c> twice.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when the bound options cannot be used.</exception>
    public static IServiceCollection AddBsftIdempotency(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftIdempotencyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return Add(
            services,
            options =>
            {
                IdempotencyConfigurationBinder.Bind(configuration, options);
                configure?.Invoke(options);
            });
    }

    /// <summary>Registers everything both overloads register, from one composed configuration delegate.</summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">What shapes the options, whatever it was built from.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// The eagerly built instance is registered nowhere; it exists so the same rules run here, where
    /// a bad configuration fails the wiring line rather than the first protected request.
    /// </remarks>
    private static IServiceCollection Add(
        IServiceCollection services, Action<BsftIdempotencyOptions> configure)
    {
        var eager = new BsftIdempotencyOptions();

        configure(eager);
        Ensure(eager);

        services.AddOptions<BsftIdempotencyOptions>().Configure(configure).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IValidateOptions<BsftIdempotencyOptions>, BsftIdempotencyOptionsValidator>());

        services.AddBsftInstrumentationSource(IdempotencyTelemetry.SourceName, ObservabilitySignals.Metrics);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IdempotencyRegistrationMarker>();
        services.TryAddSingleton<IIdempotencyTelemetry, IdempotencyTelemetry>();

        AddSeams(services);

        return services;
    }

    /// <summary>
    /// Registers the replaceable collaborators, each of which a host may override by registering its
    /// own before this call.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    private static void AddSeams(IServiceCollection services)
    {
        services.TryAddSingleton<IIdempotencyAuditSink, NullIdempotencyAuditSink>();
        services.TryAddSingleton<IIdempotencyFingerprinter, IdempotencyFingerprinter>();
        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        // Scoped rather than singleton: a shared store writes through the application's own
        // DbContext, which is scoped, and the gate is what holds it.
        services.TryAddScoped<IIdempotencyGate, IdempotencyGate>();

        // Scoped for the same reason one step further out: a host's audit sink may write through a
        // scoped unit of work, and the observer is what holds it.
        services.TryAddScoped<IIdempotencyObserver, IdempotencyObserver>();
    }

    /// <summary>Runs the options validator here and now, so a misconfiguration fails the registration.</summary>
    /// <param name="options">The configured options.</param>
    /// <exception cref="InvalidOperationException">Thrown when the options cannot be used as they stand.</exception>
    private static void Ensure(BsftIdempotencyOptions options)
    {
        var result = new BsftIdempotencyOptionsValidator().Validate(Options.DefaultName, options);

        if (result.Failed)
            throw new InvalidOperationException(string.Join(" ", result.Failures));
    }
}
