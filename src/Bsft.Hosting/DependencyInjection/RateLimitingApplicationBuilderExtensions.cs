using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

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
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>AddBsftRateLimiting</c> (or <c>AddBsftDefaults</c>) was not called.
    /// </exception>
    /// <remarks>
    /// This is ASP.NET Core's rate limiting middleware. Its endpoint limiter and the
    /// <c>RateLimiterOptions.GlobalLimiter</c> each run a timer until they are disposed, and ASP.NET Core
    /// disposes neither; the container disposes both here when the host is disposed, so a stopped host
    /// does not stay reachable through them.
    /// </remarks>
    public static IApplicationBuilder UseBsftRateLimiting(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var release = app.ApplicationServices.GetService<RateLimiterRelease>()
            ?? throw new InvalidOperationException(
                "UseBsftRateLimiting needs the services of AddBsftRateLimiting (or AddBsftDefaults), which were not registered.");
        release.UseRateLimiter(app);

        return app;
    }
}
