using Bsft.Correlation;
using Bsft.Data.MultiTenancy;
using Bsft.Testing.DependencyInjection;
using Bsft.Testing.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bsft.Testing.Tests.Support;

/// <summary>
/// A minimal application exercising what the factory is supposed to arrange: the ambient contexts
/// and a database the test replaces.
/// </summary>
internal class TestAppFactory : BsftWebApplicationFactory<TestAppEntryPoint>
{
    protected override IHostBuilder CreateHostBuilder() =>
        new HostBuilder().ConfigureWebHost(webBuilder => webBuilder
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddDbContext<TestDbContext>(options =>
                    options.UseSqlServer("Server=there-is-no-server;Database=none"));
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/context", ContextAsync);
                    endpoints.MapGet("/notes", NotesAsync);
                });
            }));

    /// <summary>
    /// The content root the factory infers points at a source directory that does not exist for a
    /// test assembly, so it is pinned to the test output directory instead.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(AppContext.BaseDirectory);
        base.ConfigureWebHost(builder);
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddBsftTestDatabase<TestDbContext>($"waf-notes-{Guid.NewGuid():N}");
    }

    private static Task ContextAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var tenant = services.GetRequiredService<ITenantContext>();
        var correlation = services.GetRequiredService<ICorrelationContext>();

        return context.Response.WriteAsync(string.Join(
            '|',
            tenant.TenantId ?? "no-tenant",
            correlation.CorrelationId));
    }

    private static async Task NotesAsync(HttpContext context)
    {
        var database = context.RequestServices.GetRequiredService<TestDbContext>();
        database.Notes.Add(new TestNote { Text = "written in memory" });
        await database.SaveChangesAsync(context.RequestAborted);

        await context.Response.WriteAsync(
            $"{database.Database.ProviderName}|{await database.Notes.CountAsync(context.RequestAborted)}");
    }
}
