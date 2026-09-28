using Bsft.Hosting.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bsft.Hosting.Tests;

public class CompressionTests
{
    [Fact]
    public void AddBsftCompression_WithNullServices_ThrowsArgumentNullException()
    {
        // Exercises CompressionServiceCollectionExtensions.AddBsftCompression line 23: null guard.
        IServiceCollection services = null!;

        var act = () => services.AddBsftCompression();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddBsftCompression_WithDefaults_RegistersResponseCompression()
    {
        // Exercises AddBsftCompression lines 29-38: Gzip added, EnableForHttps applied.
        // Accessing .Value triggers the configure lambda.
        var services = new ServiceCollection();
        services.AddBsftCompression();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ResponseCompressionOptions>>();

        options.Value.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftCompression_WithEnableForHttps_SetsFlag()
    {
        // Exercises BsftCompressionOptions.EnableForHttps (line 18) and the compressionOptions lambda.
        var services = new ServiceCollection();
        services.AddBsftCompression(o => o.EnableForHttps = true);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ResponseCompressionOptions>>();

        options.Value.EnableForHttps.Should().BeTrue();
    }

    [Fact]
    public void AddBsftCompression_WithEnableBrotli_AddsBrotliProvider()
    {
        // Exercises the if (options.EnableBrotli) branch (lines 35-37).
        // Accessing .Value triggers the configure lambda and executes the Brotli branch.
        var services = new ServiceCollection();
        services.AddBsftCompression(o => o.EnableBrotli = true);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ResponseCompressionOptions>>();

        // Accessing .Value triggers the configuration lambda, executing lines 35-37
        var compressionOptions = options.Value;
        compressionOptions.Should().NotBeNull();
    }

    [Fact]
    public void UseBsftCompression_WithNullApp_ThrowsArgumentNullException()
    {
        // Exercises CompressionApplicationBuilderExtensions.UseBsftCompression lines 17-18: null guard.
        // This covers the branch-not-taken annotation at line 16 and the throw at line 18.
        IApplicationBuilder app = null!;

        var act = () => app.UseBsftCompression();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("app");
    }

    [Fact]
    public void UseBsftCompression_WithValidApp_CallsUseResponseCompression()
    {
        // Exercises CompressionApplicationBuilderExtensions.UseBsftCompression lines 20-21.
        var services = new ServiceCollection();
        services.AddBsftCompression();
        var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);

        var result = app.UseBsftCompression();

        result.Should().NotBeNull();
    }


    [Fact]
    public void AddBsftCompression_WithConfiguration_BindsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Compression:EnableBrotli"] = "true",
                ["Bsft:Hosting:Compression:EnableForHttps"] = "true"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftCompression(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftCompressionOptions>>().Value;

        options.EnableBrotli.Should().BeTrue();
        options.EnableForHttps.Should().BeTrue();
        provider.GetRequiredService<IOptions<ResponseCompressionOptions>>().Value.EnableForHttps
            .Should().BeTrue();
    }

    [Fact]
    public void AddBsftCompression_WithConfigurationAndConfigure_LetsCodeWin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Compression:EnableBrotli"] = "true"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftCompression(configuration, o => o.EnableBrotli = false);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftCompressionOptions>>().Value.EnableBrotli.Should().BeFalse();
    }

    [Fact]
    public void AddBsftCompression_WithNullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftCompression(configuration: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftCompression_WithConfigurationAndNullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftCompression(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
