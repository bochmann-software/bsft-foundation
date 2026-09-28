using Microsoft.AspNetCore.Builder;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for configuring the BSFT response compression middleware pipeline.
/// </summary>
public static class CompressionApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the BSFT response compression middleware to the request pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseBsftCompression(this IApplicationBuilder app)
    {
        if (app is null)
            throw new ArgumentNullException(nameof(app));

        return app.UseResponseCompression();
    }
}
