using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class ValidationTests
{
    [Fact]
    public void Validate_UnmappedDestinationProperties_ThrowsDescriptiveError()
    {
        var mapper = new Mapper([new InvalidProfile()]);
        var service = new MappingValidator(mapper);

        var act = () => service.Validate();

        act.Should().Throw<MappingValidationException>()
            .Which.Errors.Should().Contain(e => e.Contains("FullName"))
            .And.Contain(e => e.Contains("Unmapped"));
    }

    [Fact]
    public void Validate_AllPropertiesMapped_DoesNotThrow()
    {
        var mapper = new Mapper([new ConventionProfile()]);
        var service = new MappingValidator(mapper);

        var act = () => service.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_AMemberTheDestinationComputes_IsNotRequiredToBeMapped()
    {
        // A property no mapping can ever write is not one a profile has to account for, so
        // demanding a configuration for it would fail a start-up over nothing.
        var mapper = new Mapper([new ComputedMemberProfile()]);
        var service = new MappingValidator(mapper);

        var act = () => service.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ExplicitAndIgnored_DoesNotThrow()
    {
        var mapper = new Mapper([new ExplicitProfile()]);
        var service = new MappingValidator(mapper);

        var act = () => service.Validate();

        act.Should().NotThrow();
    }
}
