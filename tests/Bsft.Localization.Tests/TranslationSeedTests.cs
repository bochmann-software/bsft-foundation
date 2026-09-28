using Bsft.Localization.Seeding;
using FluentAssertions;
using Xunit;

namespace Bsft.Localization.Tests;

/// <summary>
/// The compiled-in seed and the builder that produces it. Writing a seed to a store is
/// <c>Bsft.Localization.EFCore</c>'s half and is covered there.
/// </summary>
public class TranslationSeedTests
{
    [Fact]
    public void TranslationSeedBuilder_BuildsSeedCorrectly()
    {
        var builder = new TranslationSeedBuilder();
        builder.ForLocale("en", b =>
        {
            b.Add("key1", "value1");
            b.Add("key2", "value2");
        });
        builder.ForLocale("de", b =>
        {
            b.Add("key1", "wert1");
        });

        var seed = builder.Build();
        var entries = seed.GetEntries();

        entries.Should().HaveCount(2);
        entries["en"].Should().HaveCount(2);
        entries["de"].Should().HaveCount(1);
    }

    [Fact]
    public void TranslationSeedBuilder_ForLocale_NullLocale_ThrowsArgumentNullException()
    {
        var builder = new TranslationSeedBuilder();

        var act = () => builder.ForLocale(null!, b => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("locale");
    }

    [Fact]
    public void TranslationSeedBuilder_ForLocale_NullConfigure_ThrowsArgumentNullException()
    {
        var builder = new TranslationSeedBuilder();

        var act = () => builder.ForLocale("en", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void TryGetValue_AKnownEntry_IsFound()
    {
        var seed = new TranslationSeed();
        seed.Add("en", "greeting", "Hello!");

        seed.TryGetValue("en", "greeting", out var value).Should().BeTrue();
        value.Should().Be("Hello!");
    }

    [Fact]
    public void TryGetValue_AnUnknownLocale_IsNotFound()
    {
        var seed = new TranslationSeed();
        seed.Add("en", "greeting", "Hello!");

        seed.TryGetValue("de", "greeting", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryGetValue_AnUnknownKeyInAKnownLocale_IsNotFound()
    {
        var seed = new TranslationSeed();
        seed.Add("en", "greeting", "Hello!");

        seed.TryGetValue("en", "farewell", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void Add_TheSameKeyTwice_KeepsTheLastValue()
    {
        var seed = new TranslationSeed();
        seed.Add("en", "greeting", "Hello!");
        seed.Add("en", "greeting", "Hi!");

        seed.TryGetValue("en", "greeting", out var value).Should().BeTrue();
        value.Should().Be("Hi!");
    }
}
