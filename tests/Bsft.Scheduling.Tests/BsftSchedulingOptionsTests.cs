using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests;

public class BsftSchedulingOptionsTests
{
    [Fact]
    public void TheDefaultsAreUsableWithoutAnyConfiguration()
    {
        var options = new BsftSchedulingOptions();

        options.PollInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.BatchSize.Should().Be(50);
        options.LeaseDuration.Should().Be(TimeSpan.FromMinutes(5));
        options.MaxAttempts.Should().Be(3);
        options.RetryDelay.Should().Be(TimeSpan.FromSeconds(30));
        options.CatchUpPolicy.Should().Be(JobCatchUpPolicy.RunAll);
        options.MaxCatchUpAge.Should().Be(TimeSpan.FromDays(1));
        options.RecurringSeedInterval.Should().Be(TimeSpan.FromMinutes(5));
        options.InstanceId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TheSectionNameIsTheFamilyPrefixSoAHostCanBindItWithoutGuessing()
    {
        BsftSchedulingOptions.SectionName.Should().Be("Bsft:Scheduling");
    }

    [Fact]
    public void EachInstanceGetsItsOwnLeaseOwnerIdentity()
    {
        // Two instances sharing an identity would make a stuck run impossible to trace back.
        new BsftSchedulingOptions().InstanceId.Should().NotBe(new BsftSchedulingOptions().InstanceId);
    }
}
