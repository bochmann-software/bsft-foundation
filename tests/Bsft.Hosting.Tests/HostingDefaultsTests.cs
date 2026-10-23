using Bsft.Hosting.BackgroundWork;
using FluentAssertions;

namespace Bsft.Hosting.Tests;

public sealed class HostingDefaultsTests
{
    [Fact]
    public void PipelineInstrumentationSourceName_WhenRead_IsTheNameTheBackgroundWorkPackageOwns() =>
        HostingDefaults.PipelineInstrumentationSourceName.Should().Be(BackgroundWorkDefaults.PipelineInstrumentationSourceName);
}
