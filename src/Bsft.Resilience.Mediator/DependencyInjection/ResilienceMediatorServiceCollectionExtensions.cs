using Bsft.Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Resilience.Mediator.DependencyInjection;

/// <summary>
/// Extension methods for registering the mediator resilience pipeline and its retry behavior.
/// </summary>
public static class ResilienceMediatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mediator resilience pipeline and the <see cref="RetryBehavior{TRequest, TResponse}"/>
    /// that retries requests marked with <see cref="RetryableAttribute"/> on transient failures.
    /// </summary>
    /// <remarks>
    /// Under <c>UseDefaults()</c> the behavior's position in the pipeline is the framework's, not the
    /// registration order: it sits outside the transaction behavior and inside audit, validation and
    /// authorization, so a retried attempt re-runs the handler and its transaction, but not
    /// validation, authorization or audit. Without <c>UseDefaults()</c>, in a hand-wired chain,
    /// registration order still decides where the behavior sits. Calling this twice registers a
    /// single behavior.
    /// </remarks>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="configure">An optional action to override the default resilience options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    public static IServiceCollection AddBsftResilienceMediator(
        this IServiceCollection services,
        Action<BsftResilienceMediatorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return AddMediatorResilience(services, configuration: null, configure);
    }

    /// <summary>
    /// Registers the mediator resilience pipeline with options bound from configuration.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Resilience:Mediator</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    public static IServiceCollection AddBsftResilienceMediator(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftResilienceMediatorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return AddMediatorResilience(services, configuration, configure);
    }

    /// <summary>Binds, validates and registers the mediator pipeline, whichever overload was called.</summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="configuration">The configuration to bind from, or <see langword="null"/>.</param>
    /// <param name="configure">Adjusts what the section bound.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    private static IServiceCollection AddMediatorResilience(
        IServiceCollection services, IConfiguration? configuration, Action<BsftResilienceMediatorOptions>? configure)
    {
        void Apply(BsftResilienceMediatorOptions target)
        {
            configuration?.GetSection(BsftResilienceMediatorOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        var options = new BsftResilienceMediatorOptions();

        Apply(options);
        options.Validate();

        services.AddOptions<BsftResilienceMediatorOptions>().Configure(Apply).ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftResilienceMediatorOptions>, BsftResilienceMediatorOptionsValidator>());

        services.AddSingleton<IMediatorResiliencePipelineProvider, MediatorResiliencePipelineProvider>();
        services.TryAddEnumerable(
            ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>)));

        return services;
    }
}
