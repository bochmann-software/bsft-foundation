using Bsft.Localization.EFCore;
using Bsft.Localization.Seeding;
using Bsft.Localization.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Localization.Tests;

public class BsftStringLocalizerFactoryTests : IDisposable
{
    private static readonly IHttpContextAccessor Requests = new HttpContextAccessor();

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly ServiceProvider _provider;
    private readonly IServiceScopeFactory _scopeFactory;

    public BsftStringLocalizerFactoryTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITranslationDbContext>(TestDbContextFactory.Create());
        _provider = services.BuildServiceProvider();
        _scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
    }

    private BsftStringLocalizerFactory CreateFactory()
    {
        var seed = new TranslationSeed();
        var options = new BsftLocalizationOptions { DefaultLocale = "en", FallbackLocale = "en" };
        return new BsftStringLocalizerFactory(_scopeFactory, Requests, seed, _cache, Options.Create(options));
    }

    [Fact]
    public void Create_ByType_ReturnsStringLocalizer()
    {
        var factory = CreateFactory();

        var localizer = factory.Create(typeof(BsftStringLocalizerFactoryTests));

        localizer.Should().NotBeNull().And.BeOfType<BsftStringLocalizer>();
    }

    [Fact]
    public void Create_ByBaseNameAndLocation_ReturnsStringLocalizer()
    {
        var factory = CreateFactory();

        var localizer = factory.Create("MyResource", "MyAssembly");

        localizer.Should().NotBeNull().And.BeOfType<BsftStringLocalizer>();
    }

    [Fact]
    public void Constructor_NullScopeFactory_ThrowsArgumentNullException()
    {
        var seed = new TranslationSeed();
        var options = new BsftLocalizationOptions { DefaultLocale = "en", FallbackLocale = "en" };

        var act = () => new BsftStringLocalizerFactory(null!, Requests, seed, _cache, Options.Create(options));

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("scopeFactory");
    }

    [Fact]
    public void Constructor_NullRequests_ThrowsArgumentNullException()
    {
        var seed = new TranslationSeed();
        var options = new BsftLocalizationOptions { DefaultLocale = "en", FallbackLocale = "en" };

        var act = () => new BsftStringLocalizerFactory(_scopeFactory, null!, seed, _cache, Options.Create(options));

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("requests");
    }

    [Fact]
    public void CreateBundleReader_ReturnsAReaderOverTheSameDependencies()
    {
        // The endpoints read through a contract of their own rather than through the factory, so the
        // factory has to be able to hand one out.
        var factory = CreateFactory();

        factory.CreateBundleReader().Should().NotBeNull().And.BeOfType<BsftStringLocalizer>();
    }

    [Fact]
    public void Constructor_NullSeed_ThrowsArgumentNullException()
    {
        var options = new BsftLocalizationOptions { DefaultLocale = "en", FallbackLocale = "en" };

        var act = () => new BsftStringLocalizerFactory(_scopeFactory, Requests, null!, _cache, Options.Create(options));

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("seed");
    }

    [Fact]
    public void Constructor_NullCache_ThrowsArgumentNullException()
    {
        var seed = new TranslationSeed();
        var options = new BsftLocalizationOptions { DefaultLocale = "en", FallbackLocale = "en" };

        var act = () => new BsftStringLocalizerFactory(_scopeFactory, Requests, seed, null!, Options.Create(options));

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cache");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var seed = new TranslationSeed();

        var act = () => new BsftStringLocalizerFactory(_scopeFactory, Requests, seed, _cache, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("localeOptions");
    }

    public void Dispose()
    {
        _provider.Dispose();
        _cache.Dispose();
    }
}
