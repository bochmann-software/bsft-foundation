using Microsoft.AspNetCore.Builder;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for configuring the BSFT CORS middleware pipeline.
/// </summary>
public static class CorsApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the BSFT CORS middleware to the request pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
    public static IApplicationBuilder UseBsftCors(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseCors(HostingDefaults.CorsPolicyName);
    }
}
