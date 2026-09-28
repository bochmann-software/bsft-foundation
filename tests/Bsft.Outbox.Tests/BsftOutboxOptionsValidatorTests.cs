using FluentAssertions;
using Xunit;

namespace Bsft.Outbox.Tests;

/// <summary>Tests what the host is told at start about bounds a pass cannot work within.</summary>
public sealed class BsftOutboxOptionsValidatorTests
{
    [Fact]
    public void Validate_WithTheDefaults_Succeeds()
    {
        new BsftOutboxOptionsValidator()
            .Validate("primary", new BsftOutboxOptions())
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithABoundOutsideWhatItAllows_FailsNamingTheOutboxAndTheBound()
    {
        var result = new BsftOutboxOptionsValidator()
            .Validate("primary", new BsftOutboxOptions { BatchSize = 0 });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("primary").And.Contain("batch size");
    }

    /// <summary>The unnamed instance has no name to put in front of the reason.</summary>
    [Fact]
    public void Validate_WithoutAName_FailsWithTheReasonAlone()
    {
        var result = new BsftOutboxOptionsValidator()
            .Validate(null, new BsftOutboxOptions { MaxRetryDelay = TimeSpan.Zero });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("retry ceiling").And.NotContain("named");
    }

    [Fact]
    public void Validate_WithoutOptions_ThrowsArgumentNullException()
    {
        var act = () => new BsftOutboxOptionsValidator().Validate("primary", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
