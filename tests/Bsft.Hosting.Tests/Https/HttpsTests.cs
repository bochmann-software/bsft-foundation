using Bsft.Hosting.DependencyInjection;
using Bsft.Hosting.Https;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.Tests.Https;

public class HttpsTests
{
    [Fact]
    public void AddBsftHttpsDefaults_WithNullServices_ThrowsArgumentNullException()
    {
        // Exercises HttpsServiceCollectionExtensions.AddBsftHttpsDefaults line 19: null guard.
        IServiceCollection services = null!;

        var act = () => services.AddBsftHttpsDefaults();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddBsftHttpsDefaults_WithValidServices_RegistersHstsAndHttpsRedirection()
    {
        // Exercises AddBsftHttpsDefaults lines 22-31: Hsts and HttpsRedirection configure lambdas.
        // Accessing .Value triggers the configure lambdas.
        var services = new ServiceCollection();
        services.AddBsftHttpsDefaults();

        var provider = services.BuildServiceProvider();
        var hstsOptions = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.HttpsPolicy.HstsOptions>>();

        // Accessing .Value triggers the AddHsts configure lambda (lines 22-26)
        hstsOptions.Value.MaxAge.Should().Be(TimeSpan.FromDays(365));
        hstsOptions.Value.IncludeSubDomains.Should().BeTrue();
        hstsOptions.Value.Preload.Should().BeTrue();
    }

    [Fact]
    public void AddBsftHttpsDefaults_WithValidServices_RegistersHttpsRedirectionPort443()
    {
        // Exercises AddBsftHttpsDefaults lines 28-31: HttpsRedirection configure lambda.
        var services = new ServiceCollection();
        services.AddBsftHttpsDefaults();

        var provider = services.BuildServiceProvider();
        var httpsRedirectOptions = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionOptions>>();

        // Accessing .Value triggers the AddHttpsRedirection configure lambda (lines 29-31)
        httpsRedirectOptions.Value.HttpsPort.Should().Be(443);
    }

    [Fact]
    public void UseBsftHttpsRedirection_WithNullApp_ThrowsArgumentNullException()
    {
        // Exercises HttpsApplicationBuilderExtensions.UseBsftHttpsRedirection line 18-19.
        IApplicationBuilder app = null!;

        var act = () => app.UseBsftHttpsRedirection();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("app");
    }

    [Fact]
    public void UseBsftHttpsRedirection_WhenDisabled_ReturnsAppWithoutMiddleware()
    {
        // Exercises HttpsApplicationBuilderExtensions.UseBsftHttpsRedirection lines 21-22: disabled path.
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        IApplicationBuilder app = new ApplicationBuilder(provider);

        var result = app.UseBsftHttpsRedirection(HttpsRedirectionMode.Disabled);

        result.Should().BeSameAs(app);
    }

    [Fact]
    public void UseBsftHttpsRedirection_WhenEnabled_AddsMiddleware()
    {
        // Exercises HttpsApplicationBuilderExtensions.UseBsftHttpsRedirection lines 24-27: enabled path.
        var services = new ServiceCollection();
        services.AddBsftHttpsDefaults();
        var provider = services.BuildServiceProvider();
        IApplicationBuilder app = new ApplicationBuilder(provider);

        var result = app.UseBsftHttpsRedirection(HttpsRedirectionMode.Enabled);

        result.Should().NotBeNull();
    }


    [Fact]
    public void AddBsftHttpsDefaults_WithoutConfigure_KeepsRedirectionEnabled()
    {
        var services = new ServiceCollection();

        services.AddBsftHttpsDefaults();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftHttpsOptions>>().Value.RedirectionMode
            .Should().Be(HttpsRedirectionMode.Enabled);
    }

    [Fact]
    public void AddBsftHttpsDefaults_WithConfigure_DisablesRedirection()
    {
        var services = new ServiceCollection();

        services.AddBsftHttpsDefaults(o => o.RedirectionMode = HttpsRedirectionMode.Disabled);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftHttpsOptions>>().Value.RedirectionMode
            .Should().Be(HttpsRedirectionMode.Disabled);
    }

    [Fact]
    public void AddBsftHttpsDefaults_WithConfiguration_BindsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Https:RedirectionMode"] = "Disabled"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftHttpsDefaults(configuration);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftHttpsOptions>>().Value.RedirectionMode
            .Should().Be(HttpsRedirectionMode.Disabled);
    }

    [Fact]
    public void AddBsftHttpsDefaults_WithNullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftHttpsDefaults(configuration: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftHttpsDefaults_WithConfigurationAndNullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftHttpsDefaults(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
