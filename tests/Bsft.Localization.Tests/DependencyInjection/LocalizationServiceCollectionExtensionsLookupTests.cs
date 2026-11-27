using Bsft.Localization.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Bsft.Localization.Tests.DependencyInjection;

/// <summary>
/// What <c>AddBsftLocalization</c> registers for the culture-explicit lookup, and how it resolves.
/// </summary>
public class LocalizationServiceCollectionExtensionsLookupTests
{
    [Fact]
    public void AddBsftLocalization_ITranslationLookup_ResolvesAfterRegistration()
    {
        var services = new ServiceCollection();
        services.AddBsftTranslations(_ => { });
        services.AddBsftLocalization();

        var lookup = services.BuildServiceProvider().GetService<ITranslationLookup>();

        lookup.Should().NotBeNull().And.BeOfType<TranslationLookup>();
    }

    [Fact]
    public void AddBsftLocalization_ITranslationLookup_IsASingleton()
    {
        var services = new ServiceCollection();
        services.AddBsftTranslations(_ => { });
        services.AddBsftLocalization();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<ITranslationLookup>();
        var second = provider.GetRequiredService<ITranslationLookup>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void AddBsftLocalization_ATimeProviderRegisteredBefore_IsKept()
    {
        var clock = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddBsftTranslations(_ => { });
        services.AddBsftLocalization();

        var resolved = services.BuildServiceProvider().GetRequiredService<TimeProvider>();

        resolved.Should().BeSameAs(clock);
    }

    [Fact]
    public void AddBsftLocalization_NoTimeProviderRegistered_FallsBackToTheSystemClock()
    {
        var services = new ServiceCollection();
        services.AddBsftTranslations(_ => { });
        services.AddBsftLocalization();

        var resolved = services.BuildServiceProvider().GetRequiredService<TimeProvider>();

        resolved.Should().BeSameAs(TimeProvider.System);
    }
}
