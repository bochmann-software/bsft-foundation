using Bsft.Localization.Seeding;
using FluentAssertions;
using Xunit;

namespace Bsft.Localization.Tests.Seeding;

public class LocaleBuilderTests
{
    [Fact]
    public void Add_NullKey_ThrowsArgumentNullException()
    {
        var seed = new TranslationSeed();
        var builder = new TranslationSeedBuilder();
        LocaleBuilder? capturedBuilder = null;

        builder.ForLocale("en", b => { capturedBuilder = b; });
        _ = builder.Build();

        var act = () => capturedBuilder!.Add(null!, "value");

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("key");
    }

    [Fact]
    public void Add_NullValue_ThrowsArgumentNullException()
    {
        var builder = new TranslationSeedBuilder();
        LocaleBuilder? capturedBuilder = null;

        builder.ForLocale("en", b => { capturedBuilder = b; });
        _ = builder.Build();

        var act = () => capturedBuilder!.Add("key", null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("value");
    }

    [Fact]
    public void Add_ValidKeyValue_AddsToCatalog()
    {
        var builder = new TranslationSeedBuilder();
        builder.ForLocale("en", b => b.Add("hello", "Hello!"));

        var seed = builder.Build();
        var entries = seed.GetEntries();

        entries.Should().ContainKey("en");
        entries["en"].Should().ContainKey("hello");
        entries["en"]["hello"].Should().Be("Hello!");
    }

    [Fact]
    public void Add_ReturnsThisForChaining()
    {
        var builder = new TranslationSeedBuilder();
        LocaleBuilder? capturedBuilder = null;
        LocaleBuilder? returnedBuilder = null;

        builder.ForLocale("en", b =>
        {
            capturedBuilder = b;
            returnedBuilder = b.Add("key", "value");
        });
        _ = builder.Build();

        returnedBuilder.Should().BeSameAs(capturedBuilder);
    }

    [Fact]
    public void FromEmbeddedJson_NullAssembly_ThrowsArgumentNullException()
    {
        var builder = new TranslationSeedBuilder();
        LocaleBuilder? capturedBuilder = null;

        builder.ForLocale("en", b => { capturedBuilder = b; });
        _ = builder.Build();

        var act = () => capturedBuilder!.FromEmbeddedJson(null!, "prefix");

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("assembly");
    }

    [Fact]
    public void FromEmbeddedJson_NullResourcePrefix_ThrowsArgumentNullException()
    {
        var builder = new TranslationSeedBuilder();
        LocaleBuilder? capturedBuilder = null;

        builder.ForLocale("en", b => { capturedBuilder = b; });
        _ = builder.Build();

        var act = () => capturedBuilder!.FromEmbeddedJson(typeof(LocaleBuilderTests).Assembly, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("resourcePrefix");
    }

    [Fact]
    public void FromEmbeddedJson_NoMatchingResource_ThrowsInvalidOperationException()
    {
        var builder = new TranslationSeedBuilder();
        LocaleBuilder? capturedBuilder = null;

        builder.ForLocale("en", b => { capturedBuilder = b; });
        _ = builder.Build();

        var act = () => capturedBuilder!.FromEmbeddedJson(
            typeof(LocaleBuilderTests).Assembly,
            "NonExistent.Resource.Prefix");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NonExistent.Resource.Prefix*");
    }

    [Fact]
    public void FromEmbeddedJson_WithValidEmbeddedResource_LoadsEntries()
    {
        // A resource that holds a flat object contributes every one of its entries.
        // Uses the Resources/en.translations.json embedded resource.
        var builder = new TranslationSeedBuilder();

        builder.ForLocale("en", b =>
        {
            b.FromEmbeddedJson(typeof(LocaleBuilderTests).Assembly, "Bsft.Localization.Tests.Resources.en");
        });

        var seed = builder.Build();
        var entries = seed.GetEntries();

        entries.Should().ContainKey("en");
        entries["en"].Should().ContainKey("greeting");
        entries["en"]["greeting"].Should().Be("Hello!");
    }

    [Fact]
    public void FromEmbeddedJson_WithNullJsonContent_ReturnsThis()
    {
        // A resource whose content is JSON null contributes nothing, rather than throwing.
        // Uses the Resources/null.translations.json embedded resource (contains just "null").
        var builder = new TranslationSeedBuilder();

        builder.ForLocale("en", b =>
        {
            var result = b.FromEmbeddedJson(
                typeof(LocaleBuilderTests).Assembly,
                "Bsft.Localization.Tests.Resources.null");

            // Should return the builder itself (the null-entries path)
            result.Should().BeSameAs(b);
        });

        // No entries should be added for this locale
        var seed = builder.Build();
        var entries = seed.GetEntries();
        // The "en" locale may not exist or may be empty
        if (entries.ContainsKey("en"))
        {
            entries["en"].Should().BeEmpty();
        }
    }
}
