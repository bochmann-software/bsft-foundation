using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests;

public class ValidationExceptionErrorCodeTests
{
    [Fact]
    public void ValidationException_WithErrorCode_CarriesTheCustomCode()
    {
        var errors = new Dictionary<string, string[]> { ["field"] = ["error"] };
        var ex = new ValidationException("Custom validation failed", errors, "CUSTOM_VALIDATION_CODE");

        ex.Message.Should().Be("Custom validation failed");
        ex.Errors.Should().HaveCount(1);
        ex.ErrorCode.Should().Be("CUSTOM_VALIDATION_CODE");
    }

    [Fact]
    public void ValidationException_WithErrorCode_AndMultipleErrors_CarriesBoth()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["Name"] = ["Required"],
            ["Email"] = ["Invalid format", "Not unique"],
        };
        var ex = new ValidationException("Validation failed", errors, "FORM_INVALID");

        ex.Errors.Should().HaveCount(2);
        ex.ErrorCode.Should().Be("FORM_INVALID");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidationException_WithBlankErrorCode_ThrowsArgumentException(string? errorCode)
    {
        var errors = new Dictionary<string, string[]> { ["field"] = ["error"] };
        var act = () => new ValidationException("message", errors, errorCode!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidationException_WithNullErrors_ThrowsArgumentNullException()
    {
        var act = () => new ValidationException("message", null!, "CODE");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ValidationException_Errors_ReturnExactlyTheGivenEntries()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["firstName"] = ["Required"],
            ["email"] = ["Invalid format"],
            ["age"] = ["Must be positive", "Must be at most 150"]
        };
        var ex = new ValidationException("message", errors, "CODE");

        ex.Errors.Should().HaveCount(3);
        ex.Errors.Should().ContainKey("firstName");
        ex.Errors["firstName"].Should().Equal("Required");
        ex.Errors.Should().ContainKey("email");
        ex.Errors["email"].Should().Equal("Invalid format");
        ex.Errors.Should().ContainKey("age");
        ex.Errors["age"].Should().Equal("Must be positive", "Must be at most 150");

        ex.Message.Should().Be("message");
        ex.ErrorCode.Should().Be("CODE");
    }
}
