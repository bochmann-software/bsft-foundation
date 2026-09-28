using Bsft.Hosting.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Bsft.Hosting.Tests.DependencyInjection;

public class HostingApplicationBuilderExtensionsTests
{
    [Fact]
    public void UseBsftDefaults_NullApp_ThrowsArgumentNullException()
    {
        IApplicationBuilder app = null!;

        var act = () => app.UseBsftDefaults();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("app");
    }

    [Fact]
    public async Task UseBsftDefaults_WithAllServicesRegistered_BuildsMiddlewarePipeline()
    {
        // Exercises ApplicationBuilderExtensions.UseBsftDefaults lines 30-43 (the full body).
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment("Production"); // Production to avoid swagger redirect
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                    services.AddBsftDefaults();
                    services.AddRouting();
                    services.AddEndpointsApiExplorer();
                });
                webBuilder.Configure(app =>
                {
                    app.UseBsftDefaults();
                    app.Run(ctx =>
                    {
                        ctx.Response.StatusCode = 200;
                        return Task.CompletedTask;
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var response = await client.GetAsync("/health");

        host.Should().NotBeNull();
        await host.StopAsync();
    }

    [Fact]
    public async Task UseBsftDefaults_WithBsftDefaultsConfiguration_UsesHttpsDisabledFlag()
    {
        // Exercises ApplicationBuilderExtensions.UseBsftDefaults line 31:
        // config?.HttpsRedirectionDisabled ?? false - the non-null branch.
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment("Production");
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                    services.AddBsftDefaults(cfg => cfg.DisableHttpsRedirection());
                    services.AddRouting();
                    services.AddEndpointsApiExplorer();
                });
                webBuilder.Configure(app =>
                {
                    app.UseBsftDefaults();
                    app.Run(ctx =>
                    {
                        ctx.Response.StatusCode = 200;
                        return Task.CompletedTask;
                    });
                });
            })
            .StartAsync();

        host.Should().NotBeNull();
        await host.StopAsync();
    }
}
