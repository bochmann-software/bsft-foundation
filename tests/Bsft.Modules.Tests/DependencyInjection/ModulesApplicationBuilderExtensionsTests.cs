using Bsft.Modules.DependencyInjection;
using Bsft.Modules.Tests.Fakes;
using Bsft.Modules.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Bsft.Modules.Tests.DependencyInjection;

public class ModulesApplicationBuilderExtensionsTests
{
    [Fact]
    public void UseBsftModules_NullApp_ThrowsArgumentNullException()
    {
        IApplicationBuilder app = null!;

        var act = () => app.UseBsftModules();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("app");
    }

    [Fact]
    public async Task UseBsftModules_NonEndpointRouteBuilder_StartsModulesAndReturnsApp()
    {
        // IApplicationBuilder from HostBuilder+TestServer does NOT implement IEndpointRouteBuilder,
        // so only the ModuleHost.StartAsync path is taken.
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddBsftModules(
                        new ConfigurationBuilder().Build(),
                        cfg => cfg.Add<TestModuleA>());
                });
                webBuilder.Configure(app =>
                {
                    var result = app.UseBsftModules();
                    // Should return the same IApplicationBuilder for chaining
                    result.Should().BeSameAs(app);
                });
            })
            .StartAsync();

        host.Should().NotBeNull();
        await host.StopAsync();
    }

    [Fact]
    public async Task UseBsftModules_WhenAppIsEndpointRouteBuilder_MapsModulesEndpoint()
    {
        // WebApplication implements BOTH IApplicationBuilder and IEndpointRouteBuilder,
        // so calling UseBsftModules on it exercises the IEndpointRouteBuilder branch
        // and maps the /api/modules endpoint.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // No Urls - let TestServer override
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddBsftModules(
            new ConfigurationBuilder().Build(),
            cfg => cfg.Add<TestModuleA>());
        builder.Services.AddAuthorization();
        builder.Services
            .AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName, configureOptions: null);

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();
        // WebApplication IS both IApplicationBuilder and IEndpointRouteBuilder
        app.UseBsftModules();

        await app.StartAsync();

        var testServer = app.GetTestServer();
        var client = testServer.CreateClient();
        var response = await client.GetAsync("/api/modules");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("modules");

        await app.StopAsync();
        await app.DisposeAsync();
    }

    /// <summary>The inventory is not something a caller who cannot authenticate gets to read.</summary>
    [Fact]
    public async Task UseBsftModules_WhenTheCallerIsAnonymous_DoesNotAnswerWithTheInventory()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions());

        builder.WebHost.UseTestServer();
        builder.Services.AddBsftModules(
            new ConfigurationBuilder().Build(),
            cfg => cfg.Add<TestModuleA>());
        builder.Services.AddAuthorization();
        builder.Services
            .AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, AnonymousAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName, configureOptions: null);

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseBsftModules();

        await app.StartAsync();

        var response = await app.GetTestServer().CreateClient().GetAsync("/api/modules");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);

        await app.StopAsync();
        await app.DisposeAsync();
    }
}
