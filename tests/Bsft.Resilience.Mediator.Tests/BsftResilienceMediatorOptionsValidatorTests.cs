using FluentAssertions;
using Xunit;

namespace Bsft.Resilience.Mediator.Tests;

public class BsftResilienceMediatorOptionsValidatorTests
{
    [Fact]
    public void Validate_WithTheDefaults_Succeeds()
    {
        new BsftResilienceMediatorOptionsValidator()
            .Validate(name: null, new BsftResilienceMediatorOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithAnUnusableRetryCount_FailsNamingTheMember()
    {
        var result = new BsftResilienceMediatorOptionsValidator()
            .Validate(name: null, new BsftResilienceMediatorOptions { MaxRetryAttempts = -1 });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(BsftResilienceMediatorOptions.MaxRetryAttempts));
    }

    [Fact]
    public void Validate_WithoutOptions_ThrowsArgumentNullException()
    {
        var act = () => new BsftResilienceMediatorOptionsValidator().Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
