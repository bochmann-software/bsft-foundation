using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests;

public class BsftSagaOptionsValidatorTests
{
    private readonly BsftSagaOptionsValidator _validator = new();

    [Fact]
    public void Validate_NullOptions_ThrowsArgumentNullException()
    {
        var act = () => _validator.Validate(name: null, options: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Validate_DefaultOptions_Succeeds()
    {
        var result = _validator.Validate(name: null, new BsftSagaOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_NegativePollInterval_Fails()
    {
        var options = new BsftSagaOptions();
        options.Timeouts.PollInterval = TimeSpan.FromSeconds(-1);

        var result = _validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain(nameof(SagaTimeoutOptions.PollInterval));
    }

    [Fact]
    public void Validate_BatchSizeBelowOne_Fails()
    {
        var options = new BsftSagaOptions();
        options.Timeouts.BatchSize = 0;

        var result = _validator.Validate(name: null, options);

        result.Failures.Should().ContainSingle().Which.Should().Contain(nameof(SagaTimeoutOptions.BatchSize));
    }

    [Fact]
    public void Validate_NegativeMaxCatchUpAge_Fails()
    {
        var options = new BsftSagaOptions();
        options.Timeouts.MaxCatchUpAge = TimeSpan.FromDays(-1);

        var result = _validator.Validate(name: null, options);

        result.Failures.Should().ContainSingle().Which.Should().Contain(nameof(SagaTimeoutOptions.MaxCatchUpAge));
    }

    [Fact]
    public void Validate_MaxDispatchAttemptsBelowOne_Fails()
    {
        var options = new BsftSagaOptions();
        options.Timeouts.MaxDispatchAttempts = 0;

        var result = _validator.Validate(name: null, options);

        result.Failures.Should().ContainSingle().Which.Should().Contain(nameof(SagaTimeoutOptions.MaxDispatchAttempts));
    }

    [Fact]
    public void Validate_UnknownCatchUpPolicy_Fails()
    {
        var options = new BsftSagaOptions();
        options.Timeouts.CatchUpPolicy = (SagaTimeoutCatchUpPolicy)42;

        var result = _validator.Validate(name: null, options);

        result.Failures.Should().ContainSingle().Which.Should().Contain(nameof(SagaTimeoutOptions.CatchUpPolicy));
    }

    [Fact]
    public void Validate_SeveralBrokenValues_ReportsAllOfThemAtOnce()
    {
        var options = new BsftSagaOptions();
        options.Timeouts.PollInterval = TimeSpan.FromSeconds(-1);
        options.Timeouts.BatchSize = 0;

        var result = _validator.Validate(name: null, options);

        // One restart has to name every complaint, not the first one it hit.
        result.Failures.Should().HaveCount(2);
    }
}
