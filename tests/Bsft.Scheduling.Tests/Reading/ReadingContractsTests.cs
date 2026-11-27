using System.Text.Json;
using Bsft.Scheduling.Reading;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests.Reading;

/// <summary>The shapes an operator reads the job run table through.</summary>
public sealed class ReadingContractsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 3, 0, 0, TimeSpan.Zero);

    /// <summary>A filter with nothing set narrows nothing, which is what a bare listing sends.</summary>
    [Fact]
    public void JobRunFilter_WithoutCriteria_IsEmpty()
    {
        var filter = new JobRunFilter();

        filter.JobName.Should().BeNull();
        filter.JobNamePrefix.Should().BeNull();
        filter.Status.Should().BeNull();
        filter.IsRecurring.Should().BeNull();
        filter.CorrelationId.Should().BeNull();
        filter.LeaseOwner.Should().BeNull();
        filter.MinAttemptCount.Should().BeNull();
        filter.DueAfter.Should().BeNull();
        filter.DueBefore.Should().BeNull();
        filter.SettledAfter.Should().BeNull();
        filter.SettledBefore.Should().BeNull();
    }

    /// <summary>Every criterion is carried as it was given.</summary>
    [Fact]
    public void JobRunFilter_WithCriteria_CarriesThem()
    {
        var filter = new JobRunFilter(
            "nightly-import", "nightly-", JobRunStatus.Failed, true, "corr-1", "worker-2", 3,
            Now, Now, Now, Now);

        filter.Should().Be(new JobRunFilter(
            "nightly-import", "nightly-", JobRunStatus.Failed, true, "corr-1", "worker-2", 3,
            Now, Now, Now, Now));
        filter.MinAttemptCount.Should().Be(3);
    }

    /// <summary>A listing row carries where the run stands, and deliberately no payload.</summary>
    [Fact]
    public void JobRunSummary_Always_CarriesTheRun()
    {
        var summary = new JobRunSummary(
            42, "nightly-import", Now.AddHours(-1), JobRunStatus.Failed, true, "corr-1", 3, "worker-2",
            Now.AddMinutes(-5), Now);

        summary.Id.Should().Be(42);
        summary.JobName.Should().Be("nightly-import");
        summary.DueAt.Should().Be(Now.AddHours(-1));
        summary.Status.Should().Be(JobRunStatus.Failed);
        summary.IsRecurring.Should().BeTrue();
        summary.CorrelationId.Should().Be("corr-1");
        summary.AttemptCount.Should().Be(3);
        summary.LeaseOwner.Should().Be("worker-2");
        summary.LeaseExpiresAt.Should().Be(Now.AddMinutes(-5));
        summary.SettledAt.Should().Be(Now);
    }

    /// <summary>A row without a result carries none, and its JSON has no result property at all.</summary>
    [Fact]
    public void JobRunSummary_WithoutResult_WritesNoResultProperty()
    {
        var summary = new JobRunSummary(
            42, "nightly-import", Now, JobRunStatus.Completed, false, null, 1, null, null, Now);

        summary.Result.Should().BeNull();
        JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Should().NotContain("result");
    }

    /// <summary>A recorded result is written as JSON beside its schema, not as an escaped string.</summary>
    [Fact]
    public void JobRunSummary_WithResult_WritesTheValueAsJson()
    {
        var summary = new JobRunSummary(
            42, "mail-dispatch", Now, JobRunStatus.Completed, false, null, 1, null, null, Now)
        {
            Result = TestRunResults.Of("mail-dispatch/v1", """{"sent":118}"""),
        };

        JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Should().Contain("\"result\":{\"schema\":\"mail-dispatch/v1\",\"value\":{\"sent\":118}}");
    }

    /// <summary>The arguments are what the second read adds to the first.</summary>
    [Fact]
    public void JobRunView_Always_AddsThePayloadToTheSummary()
    {
        var summary = new JobRunSummary(
            42, "nightly-import", Now, JobRunStatus.Pending, false, null, 0, null, null, null);

        var view = new JobRunView(summary, """{"since":"2026-08-19"}""");

        view.Summary.Should().BeSameAs(summary);
        view.Payload.Should().Be("""{"since":"2026-08-19"}""");
    }
}
