using Microsoft.AspNetCore.Builder;

namespace Bsft.EventAggregator.Saga.Correlation.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IApplicationBuilder"/> that carry the request's correlation id
/// into the saga engine.
/// </summary>
public static class SagaCorrelationApplicationBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="SagaCorrelationMiddleware"/>, so that a saga step triggered by an event this
    /// request publishes runs under the request's correlation id.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
    /// <remarks>
    /// Register it after <c>UseBsftCorrelation()</c>, which is what resolves the id for the request.
    /// </remarks>
    public static IApplicationBuilder UseBsftSagaCorrelation(this IApplicationBuilder app)
    {
        if (app is null)
            throw new ArgumentNullException(nameof(app));

        app.UseMiddleware<SagaCorrelationMiddleware>();
        return app;
    }
}
