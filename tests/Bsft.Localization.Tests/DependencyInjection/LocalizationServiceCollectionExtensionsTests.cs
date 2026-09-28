using Bsft.EventAggregator;
using Bsft.EventAggregator.DependencyInjection;
using Bsft.Localization.DependencyInjection;
using Bsft.Localization.Seeding;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Localization.Tests.DependencyInjection;

/// <summary>
/// What the two registration calls put in the container, and what they refuse.
/// </summary>
public class LocalizationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftTranslations_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftTranslations(_ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftTranslations_NullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftTranslations(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void AddBsftTranslations_RegistersSeedAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddBsftTranslations(builder => builder.ForLocale("en", b => b.Add("key", "value")));

        var seed = services.BuildServiceProvider().GetService<TranslationSeed>();

        seed.Should().NotBeNull();
        seed!.GetEntries().Should().ContainKey("en");
    }

    [Fact]
    public void AddBsftLocalization_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftLocalization(_ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftLocalization_NullServices_OnTheConfigurationOverload_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftLocalization(EmptyConfiguration());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftLocalization_NullConfiguration_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftLocalization((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftLocalization_WithoutAnyConfiguration_TakesTheDefaults()
    {
        var services = new ServiceCollection();
        services.AddBsftLocalization();

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<BsftLocalizationOptions>>().Value;

        options.DefaultLocale.Should().Be("en");
        options.FallbackLocale.Should().Be("en");
        options.RoutePrefix.Should().Be(LocalizationDefaults.RoutePrefix);
    }

    [Fact]
    public void AddBsftLocalization_TheDelegate_IsApplied()
    {
        var services = new ServiceCollection();
        services.AddBsftLocalization(options =>
        {
            options.DefaultLocale = "de";
            options.FallbackLocale = "fr";
            options.MaxFilterModules = 4;
            options.MaxFilterModuleNameLength = 8;
        });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<BsftLocalizationOptions>>().Value;

        options.DefaultLocale.Should().Be("de");
        options.FallbackLocale.Should().Be("fr");
        options.MaxFilterModules.Should().Be(4);
        options.MaxFilterModuleNameLength.Should().Be(8);
    }

    [Fact]
    public void AddBsftLocalization_TheSection_IsBound()
    {
        var services = new ServiceCollection();
        services.AddBsftLocalization(ConfigurationWith(new Dictionary<string, string?>
        {
            ["Bsft:Localization:DefaultLocale"] = "de",
            ["Bsft:Localization:FallbackLocale"] = "en",
            ["Bsft:Localization:RoutePrefix"] = "/i18n",
            ["Bsft:Localization:MaxFilterModules"] = "7"
        }));

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<BsftLocalizationOptions>>().Value;

        options.DefaultLocale.Should().Be("de");
        options.RoutePrefix.Should().Be("/i18n");
        options.MaxFilterModules.Should().Be(7);
    }

    [Fact]
    public void AddBsftLocalization_TheDelegate_WinsOverTheSection()
    {
        var services = new ServiceCollection();
        services.AddBsftLocalization(
            ConfigurationWith(new Dictionary<string, string?> { ["Bsft:Localization:DefaultLocale"] = "de" }),
            options => options.DefaultLocale = "fr");

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<BsftLocalizationOptions>>().Value;

        options.DefaultLocale.Should().Be("fr");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddBsftLocalization_ANonPositiveModuleCeiling_IsRefusedAtRegistration(int count)
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftLocalization(options => options.MaxFilterModules = count);

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxFilterModules*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddBsftLocalization_ANonPositiveNameLengthCeiling_IsRefusedAtRegistration(int length)
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftLocalization(options => options.MaxFilterModuleNameLength = length);

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxFilterModuleNameLength*");
    }

    [Fact]
    public void AddBsftLocalization_ABlankLocaleInTheSection_IsRefusedAtRegistration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftLocalization(
            ConfigurationWith(new Dictionary<string, string?> { ["Bsft:Localization:DefaultLocale"] = " " }));

        act.Should().Throw<InvalidOperationException>().WithMessage("*DefaultLocale*");
    }

    [Fact]
    public void AddBsftLocalization_RegistersTheValidatorForStartupValidation()
    {
        var services = new ServiceCollection();
        services.AddBsftLocalization();

        var validators = services.BuildServiceProvider()
            .GetServices<IValidateOptions<BsftLocalizationOptions>>();

        validators.Should().ContainSingle(v => v is BsftLocalizationOptionsValidator);
    }

    [Fact]
    public void AddBsftLocalization_DoesNotRegisterTheOptionsInstanceItself()
    {
        var services = new ServiceCollection();
        services.AddBsftLocalization();

        services.Should().NotContain(sd => sd.ServiceType == typeof(BsftLocalizationOptions));
    }

    [Fact]
    public void AddBsftLocalization_RegistersStringLocalizerFactory_InServiceDescriptors()
    {
        var services = new ServiceCollection();
        services.AddBsftLocalization();

        // Verified without resolving: the factory needs the TranslationSeed singleton, which only
        // AddBsftTranslations adds.
        services.Should().Contain(sd => sd.ServiceType == typeof(BsftStringLocalizerFactory));
        services.Should().Contain(sd => sd.ServiceType == typeof(IStringLocalizerFactory));
    }

    [Fact]
    public void AddBsftLocalization_RegistersTheBundleReaderOnTheSameFactoryAsTheLocalizers()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftTranslations(_ => { });
        services.AddBsftLocalization();

        using var provider = services.BuildServiceProvider();

        // One factory behind both, so the bundle the endpoints serve is resolved out of the same
        // seeds, options and cache as every per-key lookup.
        provider.GetRequiredService<ITranslationBundleReader>().Should().NotBeNull();
        provider.GetRequiredService<IStringLocalizerFactory>()
            .Should().BeSameAs(provider.GetRequiredService<BsftStringLocalizerFactory>());
    }

    [Fact]
    public void AddBsftLocalization_RegistersTheRequestAccessorTheTenantIsReadThrough()
    {
        var services = new ServiceCollection();
        services.AddBsftLocalization();

        services.Should().Contain(sd => sd.ServiceType == typeof(IHttpContextAccessor));
    }

    [Fact]
    public void AddBsftLocalization_RegistersCacheInvalidator()
    {
        var services = new ServiceCollection();
        services.AddBsftLocalization();

        services.Should().Contain(sd => sd.ServiceType == typeof(TranslationCacheInvalidator));
    }

    [Fact]
    public void AddBsftLocalization_WithSeedAndEventAggregator_ResolvesStringLocalizerFactory()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftTranslations(_ => { });
        services.AddBsftLocalization();

        var factory = services.BuildServiceProvider().GetService<IStringLocalizerFactory>();

        factory.Should().NotBeNull().And.BeOfType<BsftStringLocalizerFactory>();
    }

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    private static IConfiguration ConfigurationWith(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
