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

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Validate_WithAnUnusableRetryCount_FailsNamingTheMember(int maxRetryAttempts)
    {
        var result = new BsftResilienceMediatorOptionsValidator()
            .Validate(name: null, new BsftResilienceMediatorOptions { MaxRetryAttempts = maxRetryAttempts });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(BsftResilienceMediatorOptions.MaxRetryAttempts))
            .And.Contain("at least 1");
    }

    [Fact]
    public void Validate_WithASingleRetry_Succeeds()
    {
        new BsftResilienceMediatorOptionsValidator()
            .Validate(name: null, new BsftResilienceMediatorOptions { MaxRetryAttempts = 1 }).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithoutOptions_ThrowsArgumentNullException()
    {
        var act = () => new BsftResilienceMediatorOptionsValidator().Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
