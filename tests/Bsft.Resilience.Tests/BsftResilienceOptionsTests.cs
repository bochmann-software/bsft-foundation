using FluentAssertions;
using Xunit;

namespace Bsft.Resilience.Tests;

public class BsftResilienceOptionsTests
{
    [Fact]
    public void Defaults_MirrorPollyStandardStrategy()
    {
        var options = new BsftResilienceOptions();

        options.MaxRetryAttempts.Should().Be(3);
        options.BaseDelay.Should().Be(TimeSpan.FromSeconds(2));
        options.AttemptTimeout.Should().Be(TimeSpan.FromSeconds(10));
        options.TotalRequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
        options.FailureRatio.Should().Be(0.1);
        options.SamplingDuration.Should().Be(TimeSpan.FromSeconds(30));
        options.MinimumThroughput.Should().Be(100);
        options.BreakDuration.Should().Be(TimeSpan.FromSeconds(5));
    }
}
