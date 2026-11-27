using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;

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
            standard.Retry.ShouldHandle = RetryOnlyWhenReplayable(standard.Retry.ShouldHandle);

            standard.AttemptTimeout.Timeout = options.AttemptTimeout;
            standard.TotalRequestTimeout.Timeout = options.TotalRequestTimeout;

            standard.CircuitBreaker.FailureRatio = options.FailureRatio;
            standard.CircuitBreaker.SamplingDuration = options.SamplingDuration;
            standard.CircuitBreaker.MinimumThroughput = options.MinimumThroughput;
            standard.CircuitBreaker.BreakDuration = options.BreakDuration;
        });

        return builder;
    }

    /// <summary>
    /// Narrows a retry predicate to requests that are safe to send twice.
    /// </summary>
    /// <remarks>
    /// A retry after a lost response executes the request a second time. RFC 9110 section 9.2.2 calls GET,
    /// HEAD, OPTIONS and TRACE safe and PUT and DELETE idempotent, so all of them are replayable. POST,
    /// PATCH and any other method are not, unless the request carries an <c>Idempotency-Key</c> header
    /// (<see cref="ResilienceDefaults.IdempotencyKeyHeaderName"/>), which makes the server replay the first
    /// outcome instead of executing again. The default transient-failure predicate still decides what counts
    /// as a failure; this only adds the method gate, so a consumer that assigns
    /// <c>HttpRetryStrategyOptions.ShouldHandle</c> on the named options replaces both.
    /// </remarks>
    /// <param name="transient">The predicate that decides whether an outcome is a transient failure.</param>
    /// <returns>A predicate that is true only for a transient failure of a replayable request.</returns>
    private static Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>> RetryOnlyWhenReplayable(
        Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>> transient)
        => async args => IsReplayable(args.Context.GetRequestMessage()) && await transient(args).ConfigureAwait(false);

    private static bool IsReplayable(HttpRequestMessage? request)
        => request is null
            || request.Method == HttpMethod.Get
            || request.Method == HttpMethod.Head
            || request.Method == HttpMethod.Options
            || request.Method == HttpMethod.Trace
            || request.Method == HttpMethod.Put
            || request.Method == HttpMethod.Delete
            || request.Headers.Contains(ResilienceDefaults.IdempotencyKeyHeaderName);
}
