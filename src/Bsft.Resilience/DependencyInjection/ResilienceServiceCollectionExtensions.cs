using Bsft.Correlation.Http;
using Bsft.Correlation.Http.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Resilience.DependencyInjection;

/// <summary>
/// Extension methods for registering named HTTP clients that combine correlation ID propagation with
/// the HTTP resilience pipeline. The mediator retry behavior lives in <c>Bsft.Resilience.Mediator</c>.
/// </summary>
/// <remarks>
/// The <c>IHttpClientBuilder</c> surface is a different extension family and stays on
/// <see cref="ResilientHttpClientBuilderExtensions"/>.
/// </remarks>
public static class ResilienceServiceCollectionExtensions
{
    /// <summary>
    /// Registers a named HTTP client whose outgoing requests carry the correlation ID
    /// (via <see cref="CorrelationIdDelegatingHandler"/>) and are wrapped in the BSFT resilience
    /// pipeline (retry, circuit breaker, timeout).
    /// </summary>
    /// <remarks>
    /// The correlation handler resolves <see cref="Bsft.Correlation.ICorrelationContext"/>, which is registered by
    /// <c>AddBsftCorrelation</c>. Call that during startup so the handler can read the current ID.
    /// The resilience pipeline is the outer handler, so its retry/timeout/circuit-breaker envelope
    /// wraps the correlation handler and every outgoing attempt — including retries — carries the
    /// correlation ID. The ID is stamped on the first attempt and preserved on the retry clones.
    /// </remarks>
    /// <param name="services">The service collection to register the client with.</param>
    /// <param name="name">The logical name of the HTTP client.</param>
    /// <param name="configure">An optional action to override the default resilience options.</param>
    /// <returns>The HTTP client builder for further chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    public static IHttpClientBuilder AddBsftResilientHttpClient(
        this IServiceCollection services,
        string name,
        Action<BsftResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return AddResilientHttpClient(services, name, configuration: null, configure);
    }

    /// <summary>
    /// Registers a named resilient HTTP client whose resilience options are bound from configuration.
    /// </summary>
    /// <param name="services">The service collection to register the client with.</param>
    /// <param name="name">The logical name of the HTTP client.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Resilience</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The HTTP client builder for further chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    public static IHttpClientBuilder AddBsftResilientHttpClient(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<BsftResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return AddResilientHttpClient(services, name, configuration, configure);
    }

    /// <summary>Binds, validates and registers the named client, whichever overload was called.</summary>
    /// <param name="services">The service collection to register the client with.</param>
    /// <param name="name">The logical name of the HTTP client.</param>
    /// <param name="configuration">The configuration to bind from, or <see langword="null"/>.</param>
    /// <param name="configure">Adjusts what the section bound.</param>
    /// <returns>The HTTP client builder for further chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    private static IHttpClientBuilder AddResilientHttpClient(
        IServiceCollection services, string name, IConfiguration? configuration, Action<BsftResilienceOptions>? configure)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("The HTTP client name must be provided.", nameof(name));

        // Idempotent, so a prior AddBsftCorrelation registration is not duplicated.
        services.AddBsftCorrelationHttp();

        // Order matters: AddBsftResilience is registered first so it becomes the outer handler.
        // The retry/timeout/circuit-breaker envelope therefore wraps the correlation handler, and
        // every retried attempt still carries the correlation header.
        var builder = services.AddHttpClient(name);

        builder = configuration is null
            ? builder.AddBsftResilience(configure)
            : builder.AddBsftResilience(configuration, configure);

        return builder.AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
    }
}
