using FluentAssertions;
using Xunit;

namespace Bsft.Resilience.Tests;

public class BsftResilienceStreamOptionsTests
{
    [Fact]
    public void Defaults_AreTheDocumentedOnes()
    {
        var options = new BsftResilienceStreamOptions();

        options.MaxRetryAttempts.Should().Be(3);
        options.BaseDelay.Should().Be(TimeSpan.FromMilliseconds(200));
        options.MaxDelay.Should().Be(TimeSpan.FromSeconds(30));
        options.UseJitter.Should().BeTrue();
        options.PartialStreamPolicy.Should().Be(PartialStreamPolicy.RethrowMidStream);
        options.TimeProvider.Should().BeSameAs(TimeProvider.System);
        options.ShouldRetry.Should().NotBeNull();
    }

    [Fact]
    public void IsTransientFailure_Cancellation_IsFinal()
        => BsftResilienceStreamOptions.IsTransientFailure(new OperationCanceledException()).Should().BeFalse();

    [Fact]
    public void IsTransientFailure_OtherFailure_IsTransient()
        => BsftResilienceStreamOptions.IsTransientFailure(new TimeoutException()).Should().BeTrue();

    [Fact]
    public void Validate_Defaults_Passes()
    {
        var act = new BsftResilienceStreamOptions().Validate;

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_NegativeMaxRetryAttempts_Throws()
        => Rejects(options => options.MaxRetryAttempts = -1, nameof(BsftResilienceStreamOptions.MaxRetryAttempts));

    [Fact]
    public void Validate_NegativeBaseDelay_Throws()
        => Rejects(
            options => options.BaseDelay = TimeSpan.FromSeconds(-1), nameof(BsftResilienceStreamOptions.BaseDelay));

    [Fact]
    public void Validate_MaxDelayBelowBaseDelay_Throws()
        => Rejects(
            options => options.MaxDelay = TimeSpan.FromMilliseconds(1), nameof(BsftResilienceStreamOptions.MaxDelay));

    [Fact]
    public void Validate_UndeclaredPolicy_Throws()
        => Rejects(
            options => options.PartialStreamPolicy = (PartialStreamPolicy)42,
            nameof(BsftResilienceStreamOptions.PartialStreamPolicy));

    [Fact]
    public void Validate_NullClassifier_Throws()
        => Rejects(options => options.ShouldRetry = null!, nameof(BsftResilienceStreamOptions.ShouldRetry));

    [Fact]
    public void Validate_NullTimeProvider_Throws()
        => Rejects(options => options.TimeProvider = null!, nameof(BsftResilienceStreamOptions.TimeProvider));

    /// <summary>Asserts that one broken value is rejected with a message naming its member.</summary>
    /// <param name="break">Breaks the one value under test.</param>
    /// <param name="member">The member the message must name.</param>
    private static void Rejects(Action<BsftResilienceStreamOptions> @break, string member)
    {
        var options = new BsftResilienceStreamOptions();
        @break(options);

        var act = options.Validate;

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{member}*");
    }
}
