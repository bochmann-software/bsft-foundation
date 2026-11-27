using Bsft.Hosting.SecurityHeaders;
using Microsoft.AspNetCore.Builder;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>Extension methods for adding the BSFT security headers to the request pipeline. Named <c>Headers*</c> rather than <c>SecurityHeaders*</c>: the 40-character type-name rule leaves no room, and <c>Bsft.Security</c> already has a <c>SecurityServiceCollectionExtensions</c>.</summary>
public static class HeadersApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the middleware that sends the configured security headers on every response, unless the endpoint set
    /// them itself.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <remarks>
    /// Register it early, so error responses produced further down carry the headers. The headers come from
    /// <see cref="BsftSecurityHeadersOptions"/>; without <c>AddBsftSecurityHeaders</c> the secure defaults apply
    /// unvalidated.
    /// </remarks>
    /// <returns>The application builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
    public static IApplicationBuilder UseBsftSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
