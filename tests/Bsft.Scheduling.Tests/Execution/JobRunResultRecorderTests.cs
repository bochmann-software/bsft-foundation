using Bsft.Core;
using Bsft.Scheduling.Execution;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests.Execution;

/// <summary>How a job's result reaches the claimed run, and what happens when it does not fit.</summary>
public sealed class JobRunResultRecorderTests
{
    private readonly JobRun _run = new() { Id = 7, JobName = "mail-dispatch" };

    [Fact]
    public void Constructor_WithoutRun_Throws()
    {
        var act = () => new JobRunResultRecorder(null!, 4096);

        act.Should().Throw<ArgumentNullException>().WithParameterName("run");
    }

    [Fact]
    public void Record_WithValue_PutsItOnTheRunAsCamelCaseJson()
    {
        new JobRunResultRecorder(_run, 4096).Record("mail-dispatch/v1", new { Sent = 118, Failed = 2 });

        _run.Result.Should().Be(TestRunResults.Of("mail-dispatch/v1", """{"sent":118,"failed":2}"""));
    }

    [Fact]
    public void Record_CalledAgain_ReplacesTheEarlierResult()
    {
        var recorder = new JobRunResultRecorder(_run, 4096);

        recorder.Record("mail-dispatch/v1", new { Sent = 1 });
        recorder.Record("mail-dispatch/v1", new { Sent = 2 });

        _run.Result!.Value.GetRawText().Should().Be("""{"sent":2}""");
    }

    [Fact]
    public void Record_WithNullValue_Throws()
    {
        var act = () => new JobRunResultRecorder(_run, 4096).Record<object?>("mail-dispatch/v1", null);

        act.Should().Throw<ArgumentNullException>().WithParameterName("result");
        _run.Result.Should().BeNull();
    }

    [Fact]
    public void Record_WithBlankSchema_Throws()
    {
        var act = () => new JobRunResultRecorder(_run, 4096).Record(" ", new { Sent = 1 });

        act.Should().Throw<ArgumentException>().WithParameterName("schema");
        _run.Result.Should().BeNull();
    }

    [Fact]
    public void Record_WithValueExactlyAtTheBound_Accepts()
    {
        // {"sent":1} is ten UTF-8 bytes.
        new JobRunResultRecorder(_run, 10).Record("mail-dispatch/v1", new { Sent = 1 });

        _run.Result.Should().NotBeNull();
    }

    [Fact]
    public void Record_WithValueBeyondTheBound_RefusesAndKeepsTheEarlierResult()
    {
        var recorder = new JobRunResultRecorder(_run, 10);
        recorder.Record("mail-dispatch/v1", new { Sent = 1 });

        var act = () => recorder.Record("mail-dispatch/v1", new { Sent = 10 });

        act.Should().Throw<PayloadTooLargeException>()
            .Which.ErrorCode.Should().Be(SchedulingErrorCodes.ResultTooLarge);
        _run.Result!.Value.GetRawText().Should().Be("""{"sent":1}""");
    }
}
