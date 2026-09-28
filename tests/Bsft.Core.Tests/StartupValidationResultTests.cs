using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests;

public class StartupValidationResultTests
{
    [Fact]
    public void Success_IsValidWithNoErrorsAndNoMessage()
    {
        var result = StartupValidationResult.Success();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Message.Should().BeNull();
    }

    [Fact]
    public void Success_WithMessage_StoresMessage()
    {
        var result = StartupValidationResult.Success("all good");

        result.IsValid.Should().BeTrue();
        result.Message.Should().Be("all good");
    }

    [Fact]
    public void Failure_WithErrors_IsInvalidAndExposesErrors()
    {
        var result = StartupValidationResult.Failure("missing handler", "missing config");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().BeEquivalentTo("missing handler", "missing config");
    }

    [Fact]
    public void Failure_WithNoErrors_IsValid()
    {
        var result = StartupValidationResult.Failure();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Failure_WithWarnings_CarriesBoth()
    {
        // A validator that found both has to report both: the warning has no other way of reaching
        // the host, and the failure beside it is usually the one being fixed first.
        var result = StartupValidationResult.Failure(["missing handler"], ["a context nobody could read"]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().BeEquivalentTo("missing handler");
        result.Warnings.Should().BeEquivalentTo("a context nobody could read");
    }

    [Fact]
    public void Failure_WithNoErrorsButWarnings_StaysValidAndKeepsThem()
    {
        var result = StartupValidationResult.Failure([], ["a finding"]);

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEquivalentTo("a finding");
    }

    [Fact]
    public void Failure_WithNullErrors_ThrowsArgumentNullException()
    {
        var act = () => StartupValidationResult.Failure(null!, []);

        act.Should().Throw<ArgumentNullException>().WithParameterName("errors");
    }

    [Fact]
    public void Failure_WithNullWarnings_ThrowsArgumentNullException()
    {
        var act = () => StartupValidationResult.Failure([], null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("warnings");
    }

    [Fact]
    public void Errors_IsReadOnlyList()
    {
        var result = StartupValidationResult.Failure("e");

        result.Errors.Should().BeAssignableTo<IReadOnlyList<string>>();
    }

    [Fact]
    public void Success_WithWarnings_StaysValidAndCarriesThem()
    {
        var result = StartupValidationResult.Success("checked", ["a finding", "another"]);

        result.IsValid.Should().BeTrue();
        result.Message.Should().Be("checked");
        result.Warnings.Should().BeEquivalentTo("a finding", "another");
    }

    [Fact]
    public void Warnings_WithoutAny_IsEmpty()
    {
        StartupValidationResult.Success().Warnings.Should().BeEmpty();
        StartupValidationResult.Failure("e").Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Success_WithNullWarnings_Throws()
    {
        var act = () => StartupValidationResult.Success("checked", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("warnings");
    }
}
