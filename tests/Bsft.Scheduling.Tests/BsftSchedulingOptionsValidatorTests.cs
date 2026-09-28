using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Scheduling.Tests;

public class BsftSchedulingOptionsValidatorTests
{
    private readonly BsftSchedulingOptionsValidator _validator = new();

    [Fact]
    public void TheDefaultsPass()
    {
        Validate(new BsftSchedulingOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ANullOptionsInstanceIsRejected()
    {
        ((Action)(() => _validator.Validate(null, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveBatchSizeIsRejectedBecauseNoJobWouldEverRun(int batchSize)
    {
        ShouldFailOn(new BsftSchedulingOptions { BatchSize = batchSize }, nameof(BsftSchedulingOptions.BatchSize));
    }

    [Fact]
    public void ANonPositiveAttemptCountIsRejected()
    {
        ShouldFailOn(new BsftSchedulingOptions { MaxAttempts = 0 }, nameof(BsftSchedulingOptions.MaxAttempts));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void ANonPositivePollIntervalIsRejectedBecauseThereWouldBeNoBackoff(int seconds)
    {
        // Zero is rejected too: an empty pass would come straight back round and the dispatcher would
        // hammer the store with no backoff at all.
        ShouldFailOn(
            new BsftSchedulingOptions { PollInterval = TimeSpan.FromSeconds(seconds) },
            nameof(BsftSchedulingOptions.PollInterval));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void ANonPositiveRecurringSeedIntervalIsRejectedBecauseTheReSeedWouldNeverSleep(int seconds)
    {
        ShouldFailOn(
            new BsftSchedulingOptions { RecurringSeedInterval = TimeSpan.FromSeconds(seconds) },
            nameof(BsftSchedulingOptions.RecurringSeedInterval));
    }

    [Fact]
    public void ANegativeRetryDelayIsRejected()
    {
        ShouldFailOn(
            new BsftSchedulingOptions { RetryDelay = TimeSpan.FromSeconds(-1) },
            nameof(BsftSchedulingOptions.RetryDelay));
    }

    [Fact]
    public void ANegativeCatchUpAgeIsRejected()
    {
        ShouldFailOn(
            new BsftSchedulingOptions { MaxCatchUpAge = TimeSpan.FromDays(-1) },
            nameof(BsftSchedulingOptions.MaxCatchUpAge));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveLeaseIsRejectedBecauseEveryRunWouldLookAbandoned(int minutes)
    {
        ShouldFailOn(
            new BsftSchedulingOptions { LeaseDuration = TimeSpan.FromMinutes(minutes) },
            nameof(BsftSchedulingOptions.LeaseDuration));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyInstanceIdIsRejected(string instanceId)
    {
        ShouldFailOn(
            new BsftSchedulingOptions { InstanceId = instanceId },
            nameof(BsftSchedulingOptions.InstanceId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void ANonPositiveStoredScheduleSeedIntervalIsRejected(int seconds)
    {
        ShouldFailOn(
            new BsftSchedulingOptions { StoredScheduleSeedInterval = TimeSpan.FromSeconds(seconds) },
            nameof(BsftSchedulingOptions.StoredScheduleSeedInterval));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveStoredScheduleBatchSizeIsRejectedBecauseNoStoredScheduleWouldEverRun(int batchSize)
    {
        ShouldFailOn(
            new BsftSchedulingOptions { StoredScheduleBatchSize = batchSize },
            nameof(BsftSchedulingOptions.StoredScheduleBatchSize));
    }

    [Fact]
    public void AZeroRetryDelayAndCatchUpAgeAreAllowed()
    {
        // Retrying at once and a zero catch-up window are both legitimate configurations; only the
        // poll interval has to be positive.
        var options = new BsftSchedulingOptions
        {
            PollInterval = TimeSpan.FromTicks(1),
            RetryDelay = TimeSpan.Zero,
            MaxCatchUpAge = TimeSpan.Zero,
        };

        Validate(options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void EveryComplaintIsReportedAtOnceRatherThanOneRestartAtATime()
    {
        var options = new BsftSchedulingOptions
        {
            BatchSize = 0,
            MaxAttempts = 0,
            LeaseDuration = TimeSpan.Zero,
        };

        Validate(options).Failures.Should().HaveCount(3);
    }

    private ValidateOptionsResult Validate(BsftSchedulingOptions options) => _validator.Validate(null, options);

    private void ShouldFailOn(BsftSchedulingOptions options, string member)
    {
        var result = Validate(options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain(member);
    }
}
