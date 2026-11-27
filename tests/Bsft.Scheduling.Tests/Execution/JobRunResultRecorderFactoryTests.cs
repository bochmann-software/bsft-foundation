using Bsft.Core;
using Bsft.Scheduling.Execution;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Scheduling.Tests.Execution;

/// <summary>The recorder each execution hands its job, bounded by the configured size.</summary>
public sealed class JobRunResultRecorderFactoryTests
{
    [Fact]
    public void Constructor_WithoutOptions_Throws()
    {
        var act = () => new JobRunResultRecorderFactory(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Create_WithRun_AnswersARecorderBoundedByTheConfiguredSize()
    {
        var run = new JobRun { Id = 7, JobName = "report" };
        var recorder = new JobRunResultRecorderFactory(Options.Create(new BsftSchedulingOptions { MaxResultBytes = 10 }))
            .Create(run);

        recorder.Record("report/v1", new { Rows = 1 });
        var act = () => recorder.Record("report/v1", new { Rows = 10 });

        act.Should().Throw<PayloadTooLargeException>();
        run.Result!.Value.GetRawText().Should().Be("""{"rows":1}""");
    }
}
