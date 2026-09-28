using Microsoft.AspNetCore.Builder;

namespace Bsft.Correlation.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IApplicationBuilder"/> to register BSFT correlation middleware.
/// </summary>
public static class CorrelationApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the BSFT correlation ID middleware that reads or generates a correlation ID for each request.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseBsftCorrelation(this IApplicationBuilder app)
    {
        if (app is null)
            throw new ArgumentNullException(nameof(app));

        app.UseMiddleware<CorrelationIdMiddleware>();
        return app;
    }
}
