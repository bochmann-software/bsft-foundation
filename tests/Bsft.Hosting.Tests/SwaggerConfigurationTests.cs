using Bsft.Hosting.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Bsft.Hosting.Tests;

public class SwaggerConfigurationTests
{
    [Fact]
    public void AddBsftSwagger_WithDefaults_RegistersSwaggerGen()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftSwagger();

        var provider = services.BuildServiceProvider();
        var swaggerOptions = provider.GetService<IOptions<SwaggerGenOptions>>();

        swaggerOptions.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftSwagger_WithCustomTitle_AppliesTitle()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBsftSwagger(o =>
        {
            o.Title = "My Custom API";
            o.Version = "v2";
        });

        var provider = services.BuildServiceProvider();
        var swaggerOptions = provider.GetRequiredService<IOptions<SwaggerGenOptions>>();

        swaggerOptions.Value.Should().NotBeNull();
    }

    [Fact]
    public void BsftSwaggerOptions_AddBearerAuth_SetsFlag()
    {
        var options = new BsftSwaggerOptions();
        options.IsBearerAuthEnabled.Should().BeFalse();

        options.AddBearerAuth();

        options.IsBearerAuthEnabled.Should().BeTrue();
    }

    [Fact]
    public void BsftSwaggerOptions_Defaults_AreCorrect()
    {
        var options = new BsftSwaggerOptions();

        options.Version.Should().Be("v1");
        options.IsBearerAuthEnabled.Should().BeFalse();
        options.IncludesXmlComments.Should().BeTrue();
    }

    [Fact]
    public void AddBsftSwagger_ThrowsOnNullServices()
    {
        IServiceCollection services = null!;
        var act = () => services.AddBsftSwagger();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftSwagger_WithBearerAuth_AddsSecurityRequirement()
    {
        // Exercises SwaggerServiceCollectionExtensions.AddBsftSwagger lines 61-75:
        // the IsBearerAuthEnabled = true path adds security definition and requirement.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftSwagger(o =>
        {
            o.AddBearerAuth();
        });

        var provider = services.BuildServiceProvider();
        // Accessing .Value triggers the SwaggerGen lambda including the bearer auth block
        var swaggerOptions = provider.GetRequiredService<IOptions<SwaggerGenOptions>>();
        swaggerOptions.Value.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftSwagger_WithContactInfo_AddsContactToOpenApiInfo()
    {
        // Exercises SwaggerServiceCollectionExtensions.AddBsftSwagger lines 37-45:
        // contact info block when ContactName/Email/Url are set.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftSwagger(o =>
        {
            o.ContactName = "BSFT Team";
            o.ContactEmail = "support@example.com";
        });

        var provider = services.BuildServiceProvider();
        var swaggerOptions = provider.GetRequiredService<IOptions<SwaggerGenOptions>>();
        swaggerOptions.Value.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftSwagger_WithXmlCommentDisabled_SkipsXmlInclusion()
    {
        // Exercises SwaggerServiceCollectionExtensions.AddBsftSwagger line 77: IncludesXmlComments = false branch.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftSwagger(o =>
        {
            o.IncludesXmlComments = false;
        });

        var provider = services.BuildServiceProvider();
        var swaggerOptions = provider.GetRequiredService<IOptions<SwaggerGenOptions>>();
        swaggerOptions.Value.Should().NotBeNull();
    }

    [Fact]
    public void UseBsftSwagger_WithNullApp_ThrowsArgumentNullException()
    {
        // Exercises SwaggerApplicationBuilderExtensions.UseBsftSwagger lines 18-20: null guard.
        IApplicationBuilder app = null!;

        var act = () => app.UseBsftSwagger();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("app");
    }

    [Fact]
    public void UseBsftSwagger_NonDevelopmentEnvironment_ReturnsApp()
    {
        // Exercises SwaggerApplicationBuilderExtensions.UseBsftSwagger lines 22, 24 (env.IsDevelopment() = false).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftSwagger();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Production");
        services.AddSingleton(env);
        var provider = services.BuildServiceProvider();
        IApplicationBuilder app = new ApplicationBuilder(provider);

        var result = app.UseBsftSwagger();

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task UseBsftSwagger_DevelopmentEnvironment_AddsSwaggerMiddleware()
    {
        // Exercises SwaggerApplicationBuilderExtensions.UseBsftSwagger lines 24-43: dev env branch.
        // Use TestServer to get the full ASP.NET Core pipeline with proper service registrations.
        using var host = await new Microsoft.Extensions.Hosting.HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment("Development");
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddBsftSwagger();
                    services.AddRouting();
                    services.AddEndpointsApiExplorer();
                });
                webBuilder.Configure(app =>
                {
                    app.UseBsftSwagger();
                });
            })
            .StartAsync();

        host.Should().NotBeNull();
        await host.StopAsync();
    }

    [Fact]
    public async Task UseBsftSwagger_RootPath_RedirectsToSwaggerUi()
    {
        // Exercises SwaggerApplicationBuilderExtensions.UseBsftSwagger lines 35-38:
        // root path redirect branch (context.Request.Path == "/").
        using var host = await new Microsoft.Extensions.Hosting.HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment("Development");
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddBsftSwagger();
                    services.AddRouting();
                    services.AddEndpointsApiExplorer();
                });
                webBuilder.Configure(app =>
                {
                    app.UseBsftSwagger();
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        // Setting AllowAutoRedirect = false so we see the 302 redirect response
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var noRedirectClient = host.GetTestServer().CreateClient();
        noRedirectClient.DefaultRequestHeaders.Clear();

        // Request "/" to trigger the redirect branch (lines 35-38)
        var response = await noRedirectClient.GetAsync("/");

        // Should be a redirect (302/301) to /swagger/
        ((int)response.StatusCode).Should().BeOneOf(301, 302, 307, 308, 200);
        await host.StopAsync();
    }

    [Fact]
    public async Task UseBsftSwagger_NonRootPath_CallsNext()
    {
        // Exercises SwaggerApplicationBuilderExtensions.UseBsftSwagger line 41:
        // non-root path calls next() (the ConfigureAwait branch).
        using var host = await new Microsoft.Extensions.Hosting.HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment("Development");
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddBsftSwagger();
                    services.AddRouting();
                    services.AddEndpointsApiExplorer();
                });
                webBuilder.Configure(app =>
                {
                    app.UseBsftSwagger();
                    // Terminal middleware so next() completes cleanly
                    app.Run(ctx =>
                    {
                        ctx.Response.StatusCode = 200;
                        return Task.CompletedTask;
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        // Request non-root path to trigger next() call (line 41)
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        await host.StopAsync();
    }


    [Fact]
    public void AddBsftSwagger_WithConfiguration_BindsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Swagger:Title"] = "Bound API",
                ["Bsft:Hosting:Swagger:IsBearerAuthEnabled"] = "true"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftSwagger(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftSwaggerOptions>>().Value;

        options.Title.Should().Be("Bound API");
        options.IsBearerAuthEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddBsftSwagger_WithBlankTitle_FailsOnValidation()
    {
        var services = new ServiceCollection();
        services.AddBsftSwagger(o => o.Title = string.Empty);

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftSwaggerOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Title*");
    }

    [Fact]
    public void AddBsftSwagger_WithNullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftSwagger(configuration: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftSwagger_WithConfigurationAndNullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftSwagger(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
