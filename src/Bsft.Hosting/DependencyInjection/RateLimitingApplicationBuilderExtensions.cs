using Microsoft.AspNetCore.Builder;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for configuring the BSFT rate limiting middleware pipeline.
/// </summary>
public static class RateLimitingApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the BSFT rate limiting middleware to the request pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseBsftRateLimiting(this IApplicationBuilder app)
    {
        if (app is null)
            throw new ArgumentNullException(nameof(app));

        return app.UseRateLimiter();
    }
}
