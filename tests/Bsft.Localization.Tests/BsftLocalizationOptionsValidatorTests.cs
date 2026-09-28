using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Localization.Tests;

/// <summary>
/// The bounds <c>AddBsftLocalization</c> holds its options to, stated once on the options type and
/// run twice: eagerly at registration, and again through the options infrastructure on start.
/// </summary>
public class BsftLocalizationOptionsValidatorTests
{
    [Fact]
    public void Validate_TheDefaults_AreUsable()
    {
        var act = () => new BsftLocalizationOptions().Validate();

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_ABlankDefaultLocale_IsRefused(string locale)
    {
        var act = () => new BsftLocalizationOptions { DefaultLocale = locale }.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*DefaultLocale*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_ABlankFallbackLocale_IsRefused(string locale)
    {
        var act = () => new BsftLocalizationOptions { FallbackLocale = locale }.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*FallbackLocale*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("api/localization")]
    public void Validate_ARoutePrefixThatIsNotAnAbsolutePath_IsRefused(string prefix)
    {
        var act = () => new BsftLocalizationOptions { RoutePrefix = prefix }.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*RoutePrefix*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ANonPositiveModuleCeiling_IsRefused(int count)
    {
        var act = () => new BsftLocalizationOptions { MaxFilterModules = count }.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxFilterModules*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ANonPositiveNameLengthCeiling_IsRefused(int length)
    {
        var act = () => new BsftLocalizationOptions { MaxFilterModuleNameLength = length }.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxFilterModuleNameLength*");
    }

    [Fact]
    public void Validate_ThroughTheOptionsInfrastructure_AcceptsAUsableInstance()
    {
        var result = new BsftLocalizationOptionsValidator().Validate(name: null, new BsftLocalizationOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_ThroughTheOptionsInfrastructure_ReportsTheFailureRatherThanThrowing()
    {
        var options = new BsftLocalizationOptions { DefaultLocale = " " };

        var result = new BsftLocalizationOptionsValidator().Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("DefaultLocale");
    }

    [Fact]
    public void Validate_ThroughTheOptionsInfrastructure_WithoutAnInstance_Throws()
    {
        var act = () => new BsftLocalizationOptionsValidator().Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void SectionName_IsTheFamilySection()
    {
        BsftLocalizationOptions.SectionName.Should().Be("Bsft:Localization");
    }

    [Fact]
    public void Defaults_ComeFromTheDefaultsClass()
    {
        var options = new BsftLocalizationOptions();

        options.DefaultLocale.Should().Be(LocalizationDefaults.Locale);
        options.FallbackLocale.Should().Be(LocalizationDefaults.Locale);
        options.RoutePrefix.Should().Be(LocalizationDefaults.RoutePrefix);
        options.MaxFilterModules.Should().Be(32);
        options.MaxFilterModuleNameLength.Should().Be(64);
    }

    [Fact]
    public void ValidateOptionsResult_IsWhatTheInfrastructureAsksFor()
    {
        IValidateOptions<BsftLocalizationOptions> validator = new BsftLocalizationOptionsValidator();

        validator.Validate("named", new BsftLocalizationOptions()).Succeeded.Should().BeTrue();
    }
}
