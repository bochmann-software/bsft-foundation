using Bsft.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Localization.Bundles;

/// <summary>
/// The minimal API endpoints for localization: listing available locales
/// and returning all translations for a given locale as a flat JSON object.
/// </summary>
internal static class TranslationBundleEndpoints
{
    /// <summary>
    /// What a caller is told about a locale the endpoint will not resolve. It does not repeat what
    /// was asked for: the answer is read by whoever sent it, and echoing an arbitrary route segment
    /// back is how a well-meant error message becomes a way to put a caller's words on a page.
    /// </summary>
    private const string UnusableLocale =
        "The locale must be a language tag this deployment's runtime knows, such as 'de' or 'de-CH'.";

    /// <summary>
    /// Registers the localization endpoints under the configured route prefix:
    /// <list type="bullet">
    ///   <item><c>GET {prefix}</c> - returns all available locale codes (wrapped in object)</item>
    ///   <item><c>GET {prefix}/languages</c> - returns all available locale codes as a flat JSON array</item>
    ///   <item><c>GET {prefix}/{locale}</c> - returns all translations for a locale</item>
    /// </list>
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to map the endpoints on.</param>
    /// <param name="options">The options carrying the route prefix and the module-filter bounds.</param>
    internal static void Register(IEndpointRouteBuilder endpoints, BsftLocalizationOptions options)
    {
        RegisterAvailableLocales(endpoints, options.RoutePrefix);
        RegisterLanguages(endpoints, options.RoutePrefix);
        RegisterBundle(endpoints, options);
    }

    /// <summary>Registers <c>GET {prefix}</c>, which lists the available locales wrapped in an object.</summary>
    /// <param name="endpoints">The endpoint route builder to map on.</param>
    /// <param name="prefix">The configured route prefix.</param>
    private static void RegisterAvailableLocales(IEndpointRouteBuilder endpoints, string prefix)
    {
        endpoints.MapGet(prefix, (HttpContext httpContext) =>
        {
            var locales = AvailableLocales(httpContext);

            return Results.Ok(new { locales });
        })
        .WithName("GetAvailableLocales")
        .WithSummary("Returns all available locale codes")
        .WithDescription("Returns a sorted list of all locale codes found across stored translations and seed data.")
        .WithTags("Localization")
        .Produces(StatusCodes.Status200OK);
    }

    /// <summary>Registers <c>GET {prefix}/languages</c>, which lists the available locales as a flat array.</summary>
    /// <param name="endpoints">The endpoint route builder to map on.</param>
    /// <param name="prefix">The configured route prefix.</param>
    private static void RegisterLanguages(IEndpointRouteBuilder endpoints, string prefix)
    {
        endpoints.MapGet($"{prefix}/languages", (HttpContext httpContext) => Results.Ok(AvailableLocales(httpContext)))
        .WithName("GetLanguages")
        .WithSummary("Returns all available locale codes as a JSON array")
        .WithDescription("Returns a sorted, deduplicated list of all locale codes found across stored translations and seed data as a flat JSON array, e.g. [\"de\", \"en\", \"fr\"].")
        .WithTags("Localization")
        .Produces(StatusCodes.Status200OK);
    }

    /// <summary>Registers <c>GET {prefix}/{locale}</c>, the bundle itself.</summary>
    /// <param name="endpoints">The endpoint route builder to map on.</param>
    /// <param name="options">The options carrying the route prefix and the module-filter bounds.</param>
    private static void RegisterBundle(IEndpointRouteBuilder endpoints, BsftLocalizationOptions options)
    {
        endpoints.MapGet($"{options.RoutePrefix}/{{locale}}", (
            string locale,
            string? modules,
            HttpContext httpContext) =>
        {
            // The locale before the filter: it is the half of the request that decides what is
            // resolved and cached, so an unusable one is answered before anything is read.
            if (!LocaleTag.IsAcceptable(locale))
                return Results.BadRequest(new { error = UnusableLocale });

            var selection = ModuleFilterResult.Parse(modules, options);

            return selection.IsAccepted
                ? ServeBundle(locale, selection.Filter, httpContext)
                : Results.BadRequest(new { error = selection.Error });
        })
        .WithName("GetTranslationBundle")
        .WithSummary("Returns all translations for a locale as a flat JSON object")
        .WithDescription("Applies the full fallback chain (tenant store, global store, seeds, fallback locale) and returns all resolved key-value pairs for the requested locale. Carries a strong ETag over the resolved content and answers a matching If-None-Match with 304. The optional ?modules=Orders,Inventory narrows the result to the keys under modules.{Name}. of the named modules plus every shared key, meaning every key that is not under modules. at all; the narrowed view carries its own stable ETag, independent of the order the modules were named in.")
        .WithTags("Localization")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status304NotModified)
        .Produces(StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Reads the available locales, or nothing when the localization services are not registered.
    /// </summary>
    /// <param name="httpContext">The request being answered.</param>
    /// <returns>The sorted locale codes, or an empty list.</returns>
    private static IReadOnlyList<string> AvailableLocales(HttpContext httpContext)
        => BundleReader(httpContext)?.ListAvailableLocales() ?? [];

    /// <summary>
    /// Reads the bundle reader of the request, if the host registered the localization services.
    /// </summary>
    /// <param name="httpContext">The request being answered.</param>
    /// <returns>The reader, or <see langword="null"/> when nothing registered one.</returns>
    /// <remarks>
    /// Asked for rather than taken as a handler parameter, because a host that mapped these routes
    /// without calling <c>AddBsftLocalization</c> is answered with an empty result instead of an
    /// exception per request — the behaviour these routes have always had.
    /// </remarks>
    private static ITranslationBundleReader? BundleReader(HttpContext httpContext)
        => httpContext.RequestServices.GetService<ITranslationBundleReader>();

    /// <summary>
    /// Answers a bundle request that named a usable module selection.
    /// </summary>
    /// <param name="locale">The locale to resolve translations for.</param>
    /// <param name="filter">The normalised selection, possibly empty.</param>
    /// <param name="httpContext">The request being answered.</param>
    /// <returns>The bundle, or the 304 that stands in for it.</returns>
    private static IResult ServeBundle(
        string locale,
        ModuleFilter filter,
        HttpContext httpContext)
    {
        if (BundleReader(httpContext) is not { } reader)
            return Results.Ok(new Dictionary<string, string>());

        // The narrowed view is cut from the memoised full bundle rather than resolved in its own
        // right, so there is one resolution of the fallback chain behind both shapes of this
        // response and nothing extra to invalidate. An empty selection is the unfiltered response
        // unchanged, down to the entity tag it serves.
        var bundle = reader.ResolveBundle(locale);
        var entityTag = filter.VersionOf(bundle).ToContentHashEntityTag();

        // On the 304 as well as on the 200: a 304 stands in for the response it replaces and
        // has to carry the same validator, or the client cannot tell what was confirmed.
        httpContext.Response.UseRevalidation(entityTag);

        // The tag is known before anything is narrowed, so a revalidation never builds the view it
        // stands in for.
        return httpContext.Request.IsUnchangedFrom(entityTag)
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.Ok(filter.Narrow(bundle));
    }
}
