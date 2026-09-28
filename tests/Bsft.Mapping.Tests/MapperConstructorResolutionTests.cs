using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class MapperConstructorResolutionTests
{
    [Fact]
    public void Map_RecordWithDefaultParamNotInSource_UsesDefaultValue()
    {
        // A constructor parameter the source has no counterpart for is taken from its own default
        // value rather than failing the constructor.
        var profile = new NoMatchDefaultParameterProfile();
        var mapper = new Mapper([profile]);
        var source = new SourceEntity { Id = 7, FirstName = "Jane" };

        var result = mapper.Map<SourceEntity, DestinationRecordOnlyIdWithDefault>(source);

        // Id maps via convention; ExtraWithDefault has no match -> uses default "fallback"
        result.Id.Should().Be(7);
        result.ExtraWithDefault.Should().Be("fallback");
    }

    [Fact]
    public void Map_RecordWithAllParamsCoveredByCtor_SetsRemainingPropertiesAfterCtor()
    {
        // Mapper.CreateAndPopulate:
        // DestMixedCtorAndProp(string FirstName, string LastName) has ctor for FirstName+LastName.
        // Id is a settable property not in the ctor -> must be set via property setter.
        var mapper = new Mapper([new MixedConstructorAndPropProfile()]);
        var source = new SourceEntity { Id = 5, FirstName = "Alice", LastName = "Wonder" };

        var result = mapper.Map<SourceEntity, DestinationMixedConstructorAndProp>(source);

        result.FirstName.Should().Be("Alice");
        result.LastName.Should().Be("Wonder");
        result.Id.Should().Be(5);
    }

    [Fact]
    public void Map_RecordWithCustomMappingNotInCtor_SetsCustomPropAfterCtor()
    {
        // Mapper.CreateAndPopulate:
        // Custom mappings that are NOT in the ctor params -> set via property setter.
        var profile = new RecordWithCustomPropAfterConstructorProfile();
        var mapper = new Mapper([profile]);
        var source = new SourceEntity { Id = 3, FirstName = "Bob", LastName = "Smith" };

        var result = mapper.Map<SourceEntity, DestinationMixedWithCustomProp>(source);

        result.FirstName.Should().Be("Bob");
        result.FullNameProp.Should().Be("Bob Smith");
    }

    [Fact]
    public void Map_DestinationWithTwoSatisfiableConstructors_UsesTheOneWithMoreParameters()
    {
        // Both constructors resolve from the source. The wider one is tried first, whatever order the
        // type declares them in, so the first name reaches a destination that has no setter for it.
        var mapper = new Mapper([new TwoConstructorsProfile()]);
        var source = new SourceEntity { Id = 4, FirstName = "Ann" };

        var result = mapper.Map<SourceEntity, DestinationWithTwoConstructors>(source);

        result.Id.Should().Be(4);
        result.FirstName.Should().Be("Ann");
    }

    [Fact]
    public void Map_DestinationWithNoSuitableConstructor_ThrowsInvalidOperationException()
    {
        // No parameterless ctor and no ctor whose parameters all resolve -> throws.
        var profile = new NoSuitableConstructorProfile();
        var mapper = new Mapper([profile]);
        var source = new SourceEntity { Id = 1 };

        var act = () => mapper.Map<SourceEntity, DestinationWithNoSuitableConstructor>(source);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot create an instance*");
    }
}
