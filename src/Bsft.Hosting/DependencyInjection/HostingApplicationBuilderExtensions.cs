using Bsft.Correlation.DependencyInjection;
using Bsft.Hosting.Https;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering the BSFT middleware pipeline in the correct order.
/// </summary>
public static class HostingApplicationBuilderExtensions
{
    /// <summary>
    /// Registers all BSFT middleware in the correct order: HTTPS redirection, correlation,
    /// exception handling, rate limiting, CORS, compression, Swagger, and health checks.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
    /// <remarks>
    /// Whether the HTTPS pair is added is read from <see cref="BsftHttpsOptions"/>. The options are
    /// resolved with <c>GetService</c> rather than <c>GetRequiredService</c> so that a pipeline built
    /// without <c>AddBsftHttpsDefaults</c> still redirects, which is what it did before the setting had
    /// an options type: the safe default is on.
    /// </remarks>
    public static IApplicationBuilder UseBsftDefaults(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var httpsOptions = app.ApplicationServices.GetService<IOptions<BsftHttpsOptions>>();
        var httpsMode = httpsOptions?.Value.RedirectionMode ?? HttpsRedirectionMode.Enabled;

        app.UseBsftHttpsRedirection(httpsMode);
        app.UseBsftCorrelation();
        app.UseMiddleware<BsftExceptionMiddleware>();
        app.UseBsftRateLimiting();
        app.UseBsftCors();
        app.UseBsftCompression();
        app.UseBsftSwagger();
        app.UseBsftHealthChecks();

        return app;
    }
}
