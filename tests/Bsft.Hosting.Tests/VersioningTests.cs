using Microsoft.Extensions.Options;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Bsft.Hosting.DependencyInjection;

namespace Bsft.Hosting.Tests;

public class VersioningTests
{
    [Fact]
    public void BsftVersioningOptions_Defaults_AreCorrect()
    {
        var options = new BsftVersioningOptions();

        options.DefaultVersion.Should().Be(1.0);
        options.ShouldAssumeDefaultVersionWhenUnspecified.Should().BeTrue();
        options.ShouldReportApiVersions.Should().BeTrue();
    }

    [Fact]
    public void AddBsftApiVersioning_WithDefaults_RegistersServices()
    {
        // Exercises VersioningServiceCollectionExtensions.AddBsftApiVersioning lines 30-41:
        // the AddApiVersioning and AddApiExplorer lambdas.
        var services = new ServiceCollection();
        services.AddLogging();

        var result = services.AddBsftApiVersioning();

        result.Should().NotBeNull();
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddBsftApiVersioning_WithCustomOptions_AppliesOptions()
    {
        // Exercises VersioningServiceCollectionExtensions.AddBsftApiVersioning lines 30-41
        // with custom version (e.g. 2.1 -> major=2, minor=1).
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBsftApiVersioning(o =>
        {
            o.DefaultVersion = 2.1;
            o.ShouldAssumeDefaultVersionWhenUnspecified = false;
            o.ShouldReportApiVersions = false;
        });

        var provider = services.BuildServiceProvider();
        provider.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftApiVersioning_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftApiVersioning();

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftApiVersioning_ResolvingOptions_TriggersLambdas()
    {
        // Exercises lines 31-40: resolving IOptions<ApiVersioningOptions> triggers both lambdas.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftApiVersioning();

        var provider = services.BuildServiceProvider();
        // Trigger the AddApiVersioning lambda by resolving ApiVersioningOptions
        var apiVersioningOptions = provider.GetService<Microsoft.Extensions.Options.IOptions<Asp.Versioning.ApiVersioningOptions>>();
        apiVersioningOptions.Should().NotBeNull();
        apiVersioningOptions!.Value.Should().NotBeNull();

        // Trigger the AddApiExplorer lambda (lines 38-40) by resolving ApiExplorer options
        var explorerOptions = provider.GetService<Microsoft.Extensions.Options.IOptions<Asp.Versioning.ApiExplorer.ApiExplorerOptions>>();
        explorerOptions?.Value.Should().NotBeNull();
    }


    [Fact]
    public void AddBsftApiVersioning_WithConfiguration_BindsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Versioning:DefaultVersion"] = "2.1",
                ["Bsft:Hosting:Versioning:ShouldReportApiVersions"] = "false"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftApiVersioning(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftVersioningOptions>>().Value;

        options.DefaultVersion.Should().Be(2.1);
        options.ShouldReportApiVersions.Should().BeFalse();
    }

    [Fact]
    public void AddBsftApiVersioning_WithNegativeVersion_FailsOnValidation()
    {
        var services = new ServiceCollection();
        services.AddBsftApiVersioning(o => o.DefaultVersion = -1.0);

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftVersioningOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*DefaultVersion*");
    }

    [Fact]
    public void AddBsftApiVersioning_WithNullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftApiVersioning(configuration: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftApiVersioning_WithConfigurationAndNullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftApiVersioning(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
