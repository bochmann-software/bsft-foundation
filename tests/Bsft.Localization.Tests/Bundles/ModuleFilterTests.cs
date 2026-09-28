using Bsft.Localization.Bundles;
using FluentAssertions;
using Xunit;

namespace Bsft.Localization.Tests.Bundles;

/// <summary>
/// The normalisation, the bounds and the sharing rule of the <c>?modules=</c> selection.
/// </summary>
public class ModuleFilterTests
{
    [Fact]
    public void Parse_WithoutOptions_Throws()
    {
        var act = () => ModuleFilterResult.Parse("Orders", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [InlineData(" , , ")]
    public void Parse_WithNothingNamed_IsTheEmptySelection(string? raw)
    {
        var result = ModuleFilterResult.Parse(raw, new BsftLocalizationOptions());

        result.IsAccepted.Should().BeTrue("naming no module is not an error, it is no narrowing");
        result.Filter!.IsEmpty.Should().BeTrue();
    }

    [Theory]
    [InlineData("Orders,Inventory")]
    [InlineData("Inventory,Orders")]
    [InlineData(" inventory , ORDERS ")]
    [InlineData("Orders,Inventory,orders,INVENTORY")]
    public void Parse_HoweverTheModulesAreNamed_ProducesOneCanonicalForm(string raw)
    {
        var result = ModuleFilterResult.Parse(raw, new BsftLocalizationOptions());

        result.IsAccepted.Should().BeTrue();
        result.Filter!.Modules.Should().Equal("INVENTORY", "ORDERS");
    }

    [Fact]
    public void Parse_MoreDistinctModulesThanAllowed_IsRejected()
    {
        var options = new BsftLocalizationOptions { MaxFilterModules = 2 };

        var result = ModuleFilterResult.Parse("a,b,c", options);

        result.IsAccepted.Should().BeFalse();
        result.Filter.Should().BeNull();
        result.Error.Should().Contain("2").And.Contain("distinct modules");
    }

    [Fact]
    public void Parse_RepeatingOneModulePastTheCeiling_IsStillAccepted()
    {
        // The bound is on the modules asked for, not on how often the caller repeated one.
        var options = new BsftLocalizationOptions { MaxFilterModules = 1 };

        var result = ModuleFilterResult.Parse("Orders,orders,ORDERS", options);

        result.IsAccepted.Should().BeTrue();
        result.Filter!.Modules.Should().Equal("ORDERS");
    }

    [Fact]
    public void Parse_AModuleNameLongerThanAllowed_IsRejected()
    {
        var options = new BsftLocalizationOptions { MaxFilterModuleNameLength = 4 };

        var result = ModuleFilterResult.Parse("Orders", options);

        result.IsAccepted.Should().BeFalse();
        result.Error.Should().Contain("4");
    }

    [Theory]
    [InlineData("Orders.Items")]
    [InlineData("Orders:Items")]
    [InlineData("Orders Items")]
    [InlineData("Ordërs")]
    [InlineData("Orders*")]
    public void Parse_AModuleNameOutsideTheAdmittedCharacters_IsRejected(string raw)
    {
        var result = ModuleFilterResult.Parse(raw, new BsftLocalizationOptions());

        result.IsAccepted.Should().BeFalse();
        result.Error.Should().Contain("letters");
    }

    [Theory]
    [InlineData("Orders-v2")]
    [InlineData("Orders_v2")]
    [InlineData("Orders2")]
    public void Parse_AModuleNameOfAdmittedCharacters_IsAccepted(string raw)
    {
        var result = ModuleFilterResult.Parse(raw, new BsftLocalizationOptions());

        result.IsAccepted.Should().BeTrue();
    }

    [Fact]
    public void Narrow_WithoutABundle_Throws()
    {
        var filter = FilterFor("Orders");

        var act = () => filter.Narrow(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("bundle");
    }

    [Fact]
    public void VersionOf_WithoutABundle_Throws()
    {
        var filter = FilterFor("Orders");

        var act = () => filter.VersionOf(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("bundle");
    }

    [Fact]
    public void TheEmptySelection_IsTheUnfilteredResponse()
    {
        // Down to the entity tag: a request that narrows nothing is served what it was served
        // before the filter existed.
        var bundle = BundleOf(("modules.Orders.displayName", "Orders"), ("greeting", "Hello"));

        ModuleFilter.None.VersionOf(bundle).Should().Be(bundle.Version);
        ModuleFilter.None.Narrow(bundle).Should().BeSameAs(bundle.Translations);
    }

    [Fact]
    public void Narrow_KeepsTheSelectedModulesKeysAndEverythingShared()
    {
        var filter = FilterFor("orders");

        var narrowed = filter.Narrow(BundleOf(
            ("modules.Orders.displayName", "Orders"),
            ("modules.Inventory.displayName", "Inventory"),
            ("common.save", "Save"),
            ("greeting", "Hello")));

        narrowed.Keys.Should()
            .BeEquivalentTo("modules.Orders.displayName", "common.save", "greeting");
    }

    [Fact]
    public void Narrow_AKeyDirectlyUnderTheModuleNamespace_BelongsToNoModule()
    {
        // "modules.Orders" names no module by the convention, which asks for modules.{Name}.*, so it
        // is neither shared nor owned and appears in no narrowed view.
        var filter = FilterFor("Orders");

        var narrowed = filter.Narrow(BundleOf(
            ("modules.Orders", "Orders"),
            ("modules.", "nothing"),
            ("modules.Orders.displayName", "Orders")));

        narrowed.Keys.Should().Equal("modules.Orders.displayName");
    }

    [Fact]
    public void Narrow_MatchesTheModuleSegmentIrrespectiveOfCase()
    {
        // The resolved bundle compares keys ordinal-ignore-case, so the narrowing has to as well —
        // otherwise a seed written "modules.orders.*" would vanish from a view of "Orders".
        var filter = FilterFor("Orders");

        var narrowed = filter.Narrow(BundleOf(("MODULES.orders.title", "Orders")));

        narrowed.Keys.Should().Equal("MODULES.orders.title");
    }

    [Fact]
    public void Narrow_ForAModuleNothingIsKeyedUnder_KeepsOnlyTheSharedKeys()
    {
        var filter = FilterFor("Nonexistent");

        var narrowed = filter.Narrow(BundleOf(
            ("modules.Orders.displayName", "Orders"),
            ("greeting", "Hello")));

        narrowed.Keys.Should().Equal("greeting");
    }

    [Fact]
    public void VersionOf_TheSameBundle_IsStableAndDiffersFromTheUnfilteredOne()
    {
        var filter = FilterFor("Orders");
        var bundle = BundleOf(("greeting", "Hello"));

        var version = filter.VersionOf(bundle);

        filter.VersionOf(bundle).Should().Be(version);
        version.Should().NotBe(bundle.Version, "the narrowed view is a different representation");
    }

    [Fact]
    public void VersionOf_ADifferentBundleVersion_Differs()
    {
        // The narrowed version is a function of the content, through the version of the bundle it
        // was cut from: when the content moves, so does the tag the client is holding.
        var filter = FilterFor("Orders");

        var before = filter.VersionOf(new TranslationBundle(new Dictionary<string, string>(), "aaa"));
        var after = filter.VersionOf(new TranslationBundle(new Dictionary<string, string>(), "bbb"));

        after.Should().NotBe(before);
    }

    [Theory]
    [InlineData("Orders,Inventory", "Inventory,Orders")]
    [InlineData("Orders,Inventory", " inventory , ORDERS ")]
    public void VersionOf_HoweverTheModulesWereNamed_IsTheSame(string first, string second)
    {
        var bundle = BundleOf(("greeting", "Hello"));

        FilterFor(second).VersionOf(bundle).Should().Be(FilterFor(first).VersionOf(bundle));
    }

    [Fact]
    public void VersionOf_DifferentModules_Differs()
    {
        var bundle = BundleOf(("greeting", "Hello"));

        FilterFor("Inventory").VersionOf(bundle).Should().NotBe(FilterFor("Orders").VersionOf(bundle));
    }

    [Fact]
    public void None_IsTheSelectionThatNarrowsNothing()
    {
        ModuleFilter.None.IsEmpty.Should().BeTrue();
        ModuleFilter.None.Modules.Should().BeEmpty();
    }

    private static ModuleFilter FilterFor(string modules)
        => ModuleFilterResult.Parse(modules, new BsftLocalizationOptions()).Filter!;

    private static TranslationBundle BundleOf(params (string Key, string Value)[] entries)
    {
        var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            translations[entry.Key] = entry.Value;

        return new TranslationBundle(translations, "full-version");
    }
}
