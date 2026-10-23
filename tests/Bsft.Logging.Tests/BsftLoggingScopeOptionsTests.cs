using Bsft.Logging.LogFiles;
using FluentAssertions;

namespace Bsft.Logging.Tests;

/// <summary>Covers the rules of the scope options and the shape of the members that carry scope properties.</summary>
public sealed class BsftLoggingScopeOptionsTests
{
    [Fact]
    public void ValidateFileRules_WhenTheKeyListIsNull_Throws()
    {
        var options = new BsftLoggingOptions { PersistedScopeKeys = null! };

        var act = options.ValidateFileRules;

        act.Should().Throw<InvalidOperationException>().WithMessage("*PersistedScopeKeys*<null>*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void ValidateFileRules_WhenAKeyIsBlank_Throws(string? key)
    {
        var options = new BsftLoggingOptions();
        options.PersistedScopeKeys.Add(key!);

        var act = options.ValidateFileRules;

        act.Should().Throw<InvalidOperationException>().WithMessage("*PersistedScopeKeys*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateFileRules_WhenTheBudgetIsNotPositive_Throws(int budget)
    {
        var options = new BsftLoggingOptions { MaxScopePropertyChars = budget };

        var act = options.ValidateFileRules;

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxScopePropertyChars*");
    }

    [Fact]
    public void ValidateFileRules_WithConfiguredKeys_Passes()
    {
        var options = new BsftLoggingOptions();
        options.PersistedScopeKeys.Add("RunId");

        var act = options.ValidateFileRules;

        act.Should().NotThrow();
    }

    [Fact]
    public void ScopeFilter_IsAnInitPropertyAndNoConstructorOrDeconstructParameter()
    {
        var type = typeof(LogReadRequest);
        var property = type.GetProperty(nameof(LogReadRequest.ScopeFilter))!;

        type.GetConstructors().Single().GetParameters().Should().NotContain(parameter => parameter.Name == "ScopeFilter");
        type.GetMethod("Deconstruct")!.GetParameters().Should().NotContain(parameter => parameter.Name == "ScopeFilter");
        property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()
            .Should().Contain(typeof(System.Runtime.CompilerServices.IsExternalInit));
    }

    [Fact]
    public void ScopeProperties_IsAnInitPropertyAndNoConstructorOrDeconstructParameter()
    {
        var type = typeof(LogFileEntry);
        var property = type.GetProperty(nameof(LogFileEntry.ScopeProperties))!;

        type.GetConstructors().Single().GetParameters().Should().NotContain(parameter => parameter.Name == "ScopeProperties");
        type.GetMethod("Deconstruct")!.GetParameters().Should().NotContain(parameter => parameter.Name == "ScopeProperties");
        property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()
            .Should().Contain(typeof(System.Runtime.CompilerServices.IsExternalInit));
    }

    [Fact]
    public void ScopeProperties_OfAnEntryBuiltWithoutThem_IsEmpty()
    {
        var entry = new LogFileEntry(1, null, Microsoft.Extensions.Logging.LogLevel.None, null, null, "m", null, "m", true);

        entry.ScopeProperties.Should().BeEmpty();
    }
}
