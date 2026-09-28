using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace Bsft.Resilience.DependencyInjection;

/// <summary>
/// Extension methods that apply the BSFT resilience pipeline to an <see cref="IHttpClientBuilder"/>.
/// </summary>
/// <remarks>
/// A class of its own rather than part of <see cref="DependencyInjection.ResilienceServiceCollectionExtensions"/>: the
/// "exactly one entry point per package" rule is about the <see cref="IServiceCollection"/> surface,
/// and this is a different extension family, on a builder a host already holds. The values it applies
/// belong to one named client, so they are never registered in the container — a second client with a
/// different retry budget would otherwise overwrite the first.
/// </remarks>
public static class ResilientHttpClientBuilderExtensions
{
    /// <summary>
    /// Adds the standard resilience pipeline (retry, circuit breaker, and timeout) to the HTTP client,
    /// configured from <see cref="BsftResilienceOptions"/>.
    /// </summary>
    /// <param name="builder">The HTTP client builder to configure.</param>
    /// <param name="configure">An optional action to override the default resilience options.</param>
    /// <returns>The HTTP client builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    public static IHttpClientBuilder AddBsftResilience(
        this IHttpClientBuilder builder,
        Action<BsftResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return Add(builder, configuration: null, configure);
    }

    /// <summary>
    /// Adds the standard resilience pipeline with options bound from configuration.
    /// </summary>
    /// <param name="builder">The HTTP client builder to configure.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Resilience</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The HTTP client builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    public static IHttpClientBuilder AddBsftResilience(
        this IHttpClientBuilder builder,
        IConfiguration configuration,
        Action<BsftResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        return Add(builder, configuration, configure);
    }

    /// <summary>Binds, validates and applies, whichever overload was called.</summary>
    /// <param name="builder">The HTTP client builder to configure.</param>
    /// <param name="configuration">The configuration to bind from, or <see langword="null"/>.</param>
    /// <param name="configure">Adjusts what the section bound.</param>
    /// <returns>The HTTP client builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    private static IHttpClientBuilder Add(
        IHttpClientBuilder builder, IConfiguration? configuration, Action<BsftResilienceOptions>? configure)
    {
        var options = new BsftResilienceOptions();

        configuration?.GetSection(BsftResilienceOptions.SectionName).Bind(options);
        configure?.Invoke(options);
        options.Validate();

        builder.AddStandardResilienceHandler(standard =>
        {
            standard.Retry.MaxRetryAttempts = options.MaxRetryAttempts;
            standard.Retry.Delay = options.BaseDelay;
            standard.Retry.BackoffType = DelayBackoffType.Exponential;
            standard.Retry.UseJitter = true;

            standard.AttemptTimeout.Timeout = options.AttemptTimeout;
            standard.TotalRequestTimeout.Timeout = options.TotalRequestTimeout;

            standard.CircuitBreaker.FailureRatio = options.FailureRatio;
            standard.CircuitBreaker.SamplingDuration = options.SamplingDuration;
            standard.CircuitBreaker.MinimumThroughput = options.MinimumThroughput;
            standard.CircuitBreaker.BreakDuration = options.BreakDuration;
        });

        return builder;
    }
}
