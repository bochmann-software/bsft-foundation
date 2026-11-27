using Bsft.Hosting.Cors;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

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
    /// <remarks>
    /// Applies the policy under <see cref="BsftCorsOptions.PolicyName"/>, as the
    /// last <c>AddBsftCors</c> call configured it, which is <see cref="HostingDefaults.CorsPolicyName"/> unless
    /// configured otherwise, and under that default name when <c>AddBsftCors</c> was not called.
    /// </remarks>
    /// <returns>The application builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
    public static IApplicationBuilder UseBsftCors(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var registration = app.ApplicationServices.GetService<BsftCorsRegistration>();

        return app.UseCors(registration?.Options.PolicyName ?? HostingDefaults.CorsPolicyName);
    }
}
