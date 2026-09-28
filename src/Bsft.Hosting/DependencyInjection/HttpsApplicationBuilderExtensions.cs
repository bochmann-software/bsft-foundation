using Bsft.Hosting.Https;
using Microsoft.AspNetCore.Builder;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for configuring the BSFT HTTPS middleware pipeline.
/// </summary>
public static class HttpsApplicationBuilderExtensions
{
    /// <summary>
    /// Adds HTTPS redirection to the middleware pipeline unless it has been explicitly disabled.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="mode">
    /// Whether the HTTPS middleware is added. Use <see cref="HttpsRedirectionMode.Disabled"/> for
    /// hosts that terminate TLS upstream.
    /// </param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseBsftHttpsRedirection(
        this IApplicationBuilder app,
        HttpsRedirectionMode mode = HttpsRedirectionMode.Enabled)
    {
        if (app is null)
            throw new ArgumentNullException(nameof(app));

        if (mode == HttpsRedirectionMode.Disabled)
            return app;

        app.UseHsts();
        app.UseHttpsRedirection();

        return app;
    }
}
