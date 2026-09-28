using Bsft.Hosting.Cors;
using Bsft.Hosting.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.Tests.Cors;

public class CorsTests
{
    [Fact]
    public void AddBsftCors_WithDefaults_RegistersCorsPolicy()
    {
        var services = new ServiceCollection();
        services.AddBsftCors();

        var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>();

        corsOptions.Value.Should().NotBeNull();
        var policy = corsOptions.Value.GetPolicy(HostingDefaults.CorsPolicyName);
        policy.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftCors_WithDefaults_AllowsNoOrigins()
    {
        var services = new ServiceCollection();
        services.AddBsftCors();

        var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>();
        var policy = corsOptions.Value.GetPolicy(HostingDefaults.CorsPolicyName)!;

        policy.AllowAnyOrigin.Should().BeFalse();
        policy.Origins.Should().BeEmpty();
    }

    [Fact]
    public void AddBsftCors_WithOrigins_ConfiguresOrigins()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowOrigins("https://example.com", "https://test.com"));

        var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>();
        var policy = corsOptions.Value.GetPolicy(HostingDefaults.CorsPolicyName)!;

        policy.Origins.Should().Contain("https://example.com");
        policy.Origins.Should().Contain("https://test.com");
    }

    [Fact]
    public void AddBsftCors_AllowAny_AllowsAllOrigins()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowAny());

        var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>();
        var policy = corsOptions.Value.GetPolicy(HostingDefaults.CorsPolicyName)!;

        policy.AllowAnyOrigin.Should().BeTrue();
    }

    [Fact]
    public void AddBsftCors_AllowCredentials_SetsCredentialsFlag()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o
            .AllowOrigins("https://example.com")
            .AllowCredentials());

        var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>();
        var policy = corsOptions.Value.GetPolicy(HostingDefaults.CorsPolicyName)!;

        policy.SupportsCredentials.Should().BeTrue();
    }

    [Fact]
    public void AddBsftCors_ThrowsOnNullServices()
    {
        IServiceCollection services = null!;
        var act = () => services.AddBsftCors();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AllowOrigins_WithNullOrigins_ThrowsArgumentNullException()
    {
        // Exercises BsftCorsOptions.AllowOrigins line 22: null guard.
        var options = new BsftCorsOptions();

        var act = () => options.AllowOrigins(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("origins");
    }

    [Fact]
    public void UseBsftCors_WithNullApp_ThrowsArgumentNullException()
    {
        // Exercises CorsApplicationBuilderExtensions.UseBsftCors lines 16-18: null guard (branch not taken path).
        IApplicationBuilder app = null!;

        var act = () => app.UseBsftCors();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("app");
    }

    [Fact]
    public void UseBsftCors_WithValidApp_ReturnsApp()
    {
        // Exercises CorsApplicationBuilderExtensions.UseBsftCors lines 20-21.
        var services = new ServiceCollection();
        services.AddBsftCors();
        var provider = services.BuildServiceProvider();
        var app = new Microsoft.AspNetCore.Builder.ApplicationBuilder(provider);

        var result = app.UseBsftCors();

        result.Should().NotBeNull();
    }


    [Fact]
    public void AddBsftCors_WithConfiguration_BindsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Cors:Origins:0"] = "https://bound.example",
                ["Bsft:Hosting:Cors:AreCredentialsAllowed"] = "true"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftCors(configuration);

        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IOptions<CorsOptions>>()
            .Value.GetPolicy(HostingDefaults.CorsPolicyName)!;

        policy.Origins.Should().ContainSingle().Which.Should().Be("https://bound.example");
        policy.SupportsCredentials.Should().BeTrue();
    }

    [Fact]
    public void AddBsftCors_WithConfigurationAndConfigure_LetsCodeWin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Cors:Origins:0"] = "https://bound.example"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftCors(configuration, o => o.AllowOrigins("https://code.example"));

        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IOptions<CorsOptions>>()
            .Value.GetPolicy(HostingDefaults.CorsPolicyName)!;

        policy.Origins.Should().BeEquivalentTo(["https://bound.example", "https://code.example"]);
    }

    [Fact]
    public void AddBsftCors_WithNullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftCors(configuration: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftCors_WithConfigurationAndNullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftCors(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftCors_WithBlankOrigin_FailsOnValidation()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowOrigins("   "));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftCorsOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Origins[0]*");
    }
}
