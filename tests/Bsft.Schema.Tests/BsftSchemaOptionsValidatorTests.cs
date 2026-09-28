using FluentAssertions;
using Xunit;

namespace Bsft.Schema.Tests;

/// <summary>The bounds a host may configure, and the ones it may not.</summary>
public sealed class BsftSchemaOptionsValidatorTests
{
    [Fact]
    public void Validate_WithTheShippedBounds_Succeeds()
    {
        new BsftSchemaOptionsValidator().Validate(name: null, new BsftSchemaOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithoutOptions_Refuses()
    {
        var act = () => new BsftSchemaOptionsValidator().Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
