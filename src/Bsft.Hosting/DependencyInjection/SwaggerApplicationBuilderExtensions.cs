using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for configuring the BSFT Swagger middleware pipeline.
/// </summary>
public static class SwaggerApplicationBuilderExtensions
{
    /// <summary>
    /// Enables Swagger UI in the middleware pipeline when the application is running in Development mode.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseBsftSwagger(this IApplicationBuilder app)
    {
        if (app is null)
            throw new ArgumentNullException(nameof(app));

        var env = app.ApplicationServices.GetRequiredService<IHostEnvironment>();

        if (env.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = "swagger";
            });

            // Redirect root to Swagger UI so developers land there immediately
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/" || context.Request.Path == string.Empty)
                {
                    context.Response.Redirect("/swagger/");
                    return;
                }

                await next().ConfigureAwait(false);
            });
        }

        return app;
    }
}
