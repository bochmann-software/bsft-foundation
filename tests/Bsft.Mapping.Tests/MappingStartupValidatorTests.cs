using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class MappingStartupValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WithCompleteMappings_ReturnsSuccess()
    {
        var mapper = new Mapper([new ConventionProfile()]);
        var validationService = new MappingValidator(mapper);
        var validator = new MappingStartupValidator(validationService);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithUnmappedProperty_ReturnsFailure()
    {
        var mapper = new Mapper([new InvalidProfile()]);
        var validationService = new MappingValidator(mapper);
        var validator = new MappingStartupValidator(validationService);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("FullName"));
        result.Errors.Should().Contain(e => e.Contains("Unmapped"));
    }
}
