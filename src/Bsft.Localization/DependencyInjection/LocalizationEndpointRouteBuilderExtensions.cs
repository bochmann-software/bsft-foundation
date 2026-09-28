using Bsft.Localization.Bundles;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bsft.Localization.DependencyInjection;

/// <summary>
/// Extension methods for mapping the Bsft localization endpoints onto an application's routes.
/// </summary>
public static class LocalizationEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the localization endpoints under <see cref="BsftLocalizationOptions.RoutePrefix"/>:
    /// the available locales (wrapped and as a flat array) and the translation bundle of one locale.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to map the endpoints on.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is null.</exception>
    /// <remarks>
    /// The prefix and the module-filter bounds are read once, here, because a route table is built at
    /// start and a bound that changed afterwards could not move the routes it shaped.
    /// </remarks>
    public static IEndpointRouteBuilder MapBsftLocalization(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<BsftLocalizationOptions>>().Value;

        TranslationBundleEndpoints.Register(endpoints, options);

        return endpoints;
    }
}
