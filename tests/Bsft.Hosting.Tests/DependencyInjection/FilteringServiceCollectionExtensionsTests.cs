using Bsft.Hosting.DependencyInjection;
using Bsft.Hosting.Filtering;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Hosting.Tests.DependencyInjection;

public class FilteringServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftFilterTransport_NullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftFilterTransport();

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftFilterTransport_InvalidBounds_ThrowsEagerly()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftFilterTransport(o => o.MaxDepth = 0);

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxDepth*");
    }

    [Fact]
    public void AddBsftFilterTransport_RegistersParserAndOptions()
    {
        var services = new ServiceCollection();

        services.AddBsftFilterTransport(o => o.MaxNodeCount = 42);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFilterQueryParser>().Should().BeOfType<FilterQueryParser>();
        provider.GetRequiredService<IOptions<BsftFilterTransportOptions>>().Value.MaxNodeCount.Should().Be(42);
    }

    [Fact]
    public void AddBsftFilterTransport_InsertsProviderIntoMvcOptions()
    {
        var services = new ServiceCollection();
        services.AddOptions();

        services.AddBsftFilterTransport();

        using var provider = services.BuildServiceProvider();
        var mvcOptions = provider.GetRequiredService<IOptions<MvcOptions>>().Value;
        mvcOptions.ModelBinderProviders.OfType<FilterNodeModelBinderProvider>().Should().HaveCount(1);
    }

    [Fact]
    public void AddBsftFilterTransport_CalledTwice_InsertsProviderOnce()
    {
        var services = new ServiceCollection();
        services.AddOptions();

        services.AddBsftFilterTransport();
        services.AddBsftFilterTransport();

        using var provider = services.BuildServiceProvider();
        var mvcOptions = provider.GetRequiredService<IOptions<MvcOptions>>().Value;
        mvcOptions.ModelBinderProviders.OfType<FilterNodeModelBinderProvider>().Should().HaveCount(1);
    }

    [Fact]
    public void BsftDefaultsConfiguration_FilterTransport_NullConfigure_Throws()
    {
        var config = new BsftDefaultsConfiguration();

        var act = () => config.FilterTransport(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void BsftDefaultsConfiguration_FilterTransport_StoresConfigureAndChains()
    {
        var config = new BsftDefaultsConfiguration();

        var returned = config.FilterTransport(o => o.MaxDepth = 5);

        returned.Should().BeSameAs(config);
    }


    [Fact]
    public void AddBsftFilterTransport_WithConfiguration_BindsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Filtering:MaxNodeCount"] = "11",
                ["Bsft:Hosting:Filtering:MaxDepth"] = "3"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftFilterTransport(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftFilterTransportOptions>>().Value;

        options.MaxNodeCount.Should().Be(11);
        options.MaxDepth.Should().Be(3);
    }

    [Fact]
    public void AddBsftFilterTransport_WithConfigurationCarryingInvalidBounds_ThrowsEagerly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Filtering:MaxJsonReaderDepth"] = "2"
            })
            .Build();
        var services = new ServiceCollection();

        var act = () => services.AddBsftFilterTransport(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxJsonReaderDepth*");
    }

    [Fact]
    public void AddBsftFilterTransport_WithNullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftFilterTransport(configuration: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftFilterTransport_WithConfigurationAndNullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftFilterTransport(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
