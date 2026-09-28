using Bsft.Hosting.BackgroundWork;
using Bsft.Hosting.Tests.BackgroundWork.Fakes;
using FluentAssertions;
using Xunit;

namespace Bsft.Hosting.Tests.BackgroundWork;

public sealed class BsftRunOnceOptionsTests
{
    [Fact]
    public void TheDefaultsRetryForeverOnAWideningDelay()
    {
        var options = new BsftRunOnceOptions();

        BsftRunOnceOptions.SectionName.Should().Be("Bsft:Hosting:RunOnce");
        options.StartupDelay.Should().Be(TimeSpan.Zero);
        options.InitialRetryDelay.Should().Be(TimeSpan.FromSeconds(5));
        options.MaxRetryDelay.Should().Be(TimeSpan.FromMinutes(5));
        options.BackoffFactor.Should().Be(2);
        options.MaxAttempts.Should().Be(0);
    }

    [Fact]
    public void TheOptionsNameIsTheServiceTypeFullName()
    {
        BsftRunOnceOptions.NameFor<RecordingRunOnceService>()
            .Should().Be(typeof(RecordingRunOnceService).FullName);

        BsftRunOnceOptions.NameFor(typeof(RecordingRunOnceService))
            .Should().Be(BsftRunOnceOptions.NameFor<RecordingRunOnceService>());
    }

    [Fact]
    public void TheOptionsNameRejectsAMissingType()
    {
        ((Action)(() => BsftRunOnceOptions.NameFor(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("serviceType");
    }

    [Fact]
    public void ATypeWithoutAFullNameFallsBackToItsShortName()
    {
        // A generic parameter has no full name, which is the one case the fallback exists for.
        var parameter = typeof(List<>).GetGenericArguments()[0];

        BsftRunOnceOptions.NameFor(parameter).Should().Be(parameter.Name);
    }
}
