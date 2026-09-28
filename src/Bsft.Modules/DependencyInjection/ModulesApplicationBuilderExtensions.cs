using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Modules.DependencyInjection;

/// <summary>
/// Extension methods for integrating the BSFT module system into the ASP.NET Core request pipeline.
/// </summary>
public static class ModulesApplicationBuilderExtensions
{
    /// <summary>
    /// Starts the <see cref="IModuleHost"/> by calling <see cref="IModule.OnStartAsync"/> on all modules
    /// in dependency order, and maps the <c>GET /api/modules</c> discovery endpoint.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <remarks>
    /// The discovery endpoint requires an authenticated caller. What it answers with is the inventory
    /// of an installation and the exact version of every part of it, which is what someone looking for
    /// a known weakness in one of them would ask for first, and there is nothing in it a caller who
    /// cannot authenticate needs.
    /// </remarks>
    public static IApplicationBuilder UseBsftModules(this IApplicationBuilder app)
    {
        if (app is null) throw new ArgumentNullException(nameof(app));

        var host = app.ApplicationServices.GetRequiredService<IModuleHost>();
        // Run on the thread pool (free of any ambient SynchronizationContext) before blocking, so an
        // OnStartAsync implementation that awaits the captured context cannot deadlock the caller.
        Task.Run(() => host.StartAsync(app.ApplicationServices)).GetAwaiter().GetResult();

        if (app is IEndpointRouteBuilder endpointRouteBuilder)
        {
            endpointRouteBuilder.MapGet(ModulesDefaults.DiscoveryRoute, (IModuleHost moduleHost) =>
            {
                var modules = moduleHost.ListModuleInfo();
                return Results.Ok(new { modules });
            })
            .WithName("GetModules")
            .WithSummary("Returns all registered modules with their status")
            .WithDescription("Returns the technical name, version, and enabled status of every loaded module. Display names are resolved via the localization subsystem using the convention modules.{Name}.displayName.")
            .WithTags("Modules")
            .Produces(StatusCodes.Status200OK)
            .RequireAuthorization();
        }

        return app;
    }
}
