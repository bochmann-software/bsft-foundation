using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests;

public class ValidationResultTests
{
    [Fact]
    public void Success_HasNoErrorsAndIsValid()
    {
        var result = ValidationResult.Success();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ParameterlessConstructor_IsValid()
    {
        var result = new ValidationResult();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Failure_WithErrors_IsInvalidAndExposesErrors()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["Name"] = ["Required"]
        };

        var result = ValidationResult.Failure(errors);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey("Name");
        result.Errors["Name"].Should().ContainSingle().Which.Should().Be("Required");
    }

    [Fact]
    public void Failure_WithNullErrors_ThrowsArgumentNullException()
    {
        var act = () => ValidationResult.Failure(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("errors");
    }

    [Fact]
    public void Constructor_TakesDefensiveCopy_SourceMutationDoesNotLeakIn()
    {
        var source = new Dictionary<string, string[]>
        {
            ["Name"] = ["Required"]
        };
        var result = new ValidationResult(source);

        source["Injected"] = ["hacked"];

        result.Errors.Should().ContainKey("Name");
        result.Errors.Should().NotContainKey("Injected");
    }

    [Fact]
    public void Errors_IsReadOnlyDictionary()
    {
        var result = ValidationResult.Success();

        result.Errors.Should().BeAssignableTo<IReadOnlyDictionary<string, string[]>>();
    }
}
