using Bsft.Hosting.Cors;
using Bsft.Hosting.Https;
using FluentAssertions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using IStartupValidator = Bsft.Core.IStartupValidator;
using Bsft.Hosting.DependencyInjection;

namespace Bsft.Hosting.Tests.DependencyInjection;

public class HostingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftDefaults_RegistersHealthChecks()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddBsftDefaults();

        var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetService<HealthCheckService>();

        healthCheckService.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftDefaults_RegistersStartupValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddBsftDefaults();

        var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IStartupValidator>();

        validators.Should().NotBeEmpty();
    }

    [Fact]
    public void AddBsftDefaults_RegistersCorsPolicy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddBsftDefaults();

        var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetService<IOptions<CorsOptions>>();

        corsOptions.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftDefaults_WithoutCustomConfiguration_LeavesHttpsRedirectionEnabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddBsftDefaults();

        var provider = services.BuildServiceProvider();
        var httpsOptions = provider.GetRequiredService<IOptions<BsftHttpsOptions>>();

        httpsOptions.Value.RedirectionMode.Should().Be(HttpsRedirectionMode.Enabled);
    }

    [Fact]
    public void AddBsftDefaults_WithCustomConfiguration_AppliesSettings()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddBsftDefaults(cfg => cfg
            .DisableHttpsRedirection()
            .Swagger(swagger => swagger.Title = "Orders API"));

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftHttpsOptions>>().Value.RedirectionMode
            .Should().Be(HttpsRedirectionMode.Disabled);
        provider.GetRequiredService<IOptions<BsftSwaggerOptions>>().Value.Title
            .Should().Be("Orders API");
    }

    [Fact]
    public void AddBsftDefaults_WithConfiguration_BindsEveryComponentSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Swagger:Title"] = "Bound API",
                ["Bsft:Hosting:RateLimiting:PermitLimit"] = "7",
                ["Bsft:Hosting:Https:RedirectionMode"] = "Disabled",
                ["Bsft:Hosting:Cors:Origins:0"] = "https://bound.example"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddBsftDefaults(configuration);

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftSwaggerOptions>>().Value.Title.Should().Be("Bound API");
        provider.GetRequiredService<IOptions<BsftRateLimitingOptions>>().Value.PermitLimit.Should().Be(7);
        provider.GetRequiredService<IOptions<BsftHttpsOptions>>().Value.RedirectionMode
            .Should().Be(HttpsRedirectionMode.Disabled);
        provider.GetRequiredService<IOptions<BsftCorsOptions>>().Value.Origins
            .Should().ContainSingle().Which.Should().Be("https://bound.example");
    }

    [Fact]
    public void AddBsftDefaults_WithConfigurationAndConfigure_LetsCodeWin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Swagger:Title"] = "Bound API"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddBsftDefaults(configuration, cfg => cfg.Swagger(swagger => swagger.Title = "Code API"));

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftSwaggerOptions>>().Value.Title.Should().Be("Code API");
    }

    [Fact]
    public void AddBsftDefaults_WithNullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftDefaults(configuration: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftDefaults_WithConfigurationAndNullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftDefaults(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftDefaults_ThrowsOnNullServices()
    {
        IServiceCollection services = null!;
        var act = () => services.AddBsftDefaults();
        act.Should().Throw<ArgumentNullException>();
    }
}
