using Bsft.Localization.EFCore.DependencyInjection;
using Bsft.Localization.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Localization.EFCore.Tests.DependencyInjection;

/// <summary>
/// What the single registration call contributes: the context binding and the reader behind
/// <see cref="ITranslationReader"/>.
/// </summary>
public class LocalizationEFCoreServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftLocalizationEFCore_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftLocalizationEFCore<TestTranslationDbContext>();

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftLocalizationEFCore_RegistersTheContextBindingScoped()
    {
        var services = Registered();

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITranslationDbContext) && sd.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddBsftLocalizationEFCore_RegistersTheReaderScoped()
    {
        var services = Registered();

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITranslationReader)
            && sd.ImplementationType == typeof(EFCoreTranslationReader)
            && sd.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddBsftLocalizationEFCore_TheReader_ResolvesOverTheApplicationsContext()
    {
        using var provider = Registered().BuildServiceProvider();
        using var scope = provider.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<ITranslationReader>();

        reader.Should().BeOfType<EFCoreTranslationReader>();
        reader.ListLocales().Should().BeEmpty();
    }

    [Fact]
    public void AddBsftLocalizationEFCore_CalledTwice_KeepsTheFirstRegistration()
    {
        var services = Registered();
        services.AddBsftLocalizationEFCore<TestTranslationDbContext>();

        services.Count(sd => sd.ServiceType == typeof(ITranslationReader)).Should().Be(1);
        services.Count(sd => sd.ServiceType == typeof(ITranslationDbContext)).Should().Be(1);
    }

    [Fact]
    public void AddBsftLocalizationEFCore_AHostThatRegisteredItsOwnReader_KeepsIt()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITranslationReader, StubTranslationReader>();
        services.AddDbContext<TestTranslationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftLocalizationEFCore<TestTranslationDbContext>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITranslationReader>().Should().BeOfType<StubTranslationReader>();
    }

    private static IServiceCollection Registered()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestTranslationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftLocalizationEFCore<TestTranslationDbContext>();

        return services;
    }

    private sealed class StubTranslationReader : ITranslationReader
    {
        public IReadOnlyList<Translation> ListForLocale(string locale, string? tenantId) => [];

        public IReadOnlyList<Translation> ListForKey(string key, string locale, string fallbackLocale, string? tenantId) => [];

        public IReadOnlyList<Translation> ListForLocaleOrFallback(string locale, string fallbackLocale, string? tenantId) => [];

        public IReadOnlyList<string> ListLocales() => [];
    }
}
