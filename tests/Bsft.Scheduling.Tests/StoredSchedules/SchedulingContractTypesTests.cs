using Bsft.Scheduling.StoredSchedules;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests.StoredSchedules;

public class SchedulingContractTypesTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnExecutionContextCarriesEverythingAJobNeedsToKnowAboutItsRun()
    {
        var context = new JobExecutionContext(7, "report", _now, _now.AddSeconds(2), 1, "corr-1", "payload");

        context.JobId.Should().Be(7);
        context.JobName.Should().Be("report");
        context.ScheduledFor.Should().Be(_now);
        context.StartedAt.Should().Be(_now.AddSeconds(2));
        context.AttemptNumber.Should().Be(1);
        context.CorrelationId.Should().Be("corr-1");
        context.Payload.Should().Be("payload");
    }

    [Fact]
    public void TwoExecutionContextsDescribingTheSameRunAreEqual()
    {
        var left = new JobExecutionContext(7, "report", _now, _now, 1, null, null);
        var right = new JobExecutionContext(7, "report", _now, _now, 1, null, null);

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
        left.ToString().Should().Contain("report");
    }

    [Fact]
    public void ARegistrationIsAValueDescribedByItsParts()
    {
        var schedule = JobSchedule.Every(TimeSpan.FromHours(1));
        var left = new JobDefinition("nightly", typeof(RecordingJob), schedule, JobOverlapPolicy.Allow);
        var right = new JobDefinition("nightly", typeof(RecordingJob), schedule, JobOverlapPolicy.Allow);

        left.Should().Be(right);
        left.OverlapPolicy.Should().Be(JobOverlapPolicy.Allow);
    }

    [Fact]
    public void TheNotRegisteredExceptionNamesWhatCouldNotBeResolved()
    {
        JobNotRegisteredException.ForName("typo").Message.Should().Contain("typo");
        JobNotRegisteredException.ForType(typeof(RecordingJob)).Message
            .Should().Contain(typeof(RecordingJob).FullName);
    }

    [Fact]
    public void TheNotRegisteredExceptionOffersTheStandardConstructors()
    {
        new JobNotRegisteredException().Message.Should().NotBeNull();
        new JobNotRegisteredException("missing").Message.Should().Be("missing");

        var inner = new InvalidOperationException("inner");
        var wrapped = new JobNotRegisteredException("missing", inner);

        wrapped.Message.Should().Be("missing");
        wrapped.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void AJobRunDefaultsToAPendingOneShotWithNoLease()
    {
        var run = new JobRun();

        run.Id.Should().Be(0);
        run.JobName.Should().BeEmpty();
        run.Status.Should().Be(JobRunStatus.Pending);
        run.IsRecurring.Should().BeFalse();
        run.Payload.Should().BeNull();
        run.CorrelationId.Should().BeNull();
        run.AttemptCount.Should().Be(0);
        run.LeaseOwner.Should().BeNull();
        run.LeaseExpiresAt.Should().BeNull();
        run.SettledAt.Should().BeNull();
        run.ConcurrencyToken.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AStoredScheduleDefaultsToAnEnabledEveryMinuteInterval()
    {
        var schedule = new StoredSchedule();

        schedule.Id.Should().Be(0);
        schedule.TenantId.Should().BeNull();
        schedule.JobKey.Should().BeEmpty();
        schedule.Schedule.Interval.Should().Be(TimeSpan.FromMinutes(1));
        schedule.Payload.Should().BeNull();
        schedule.Enabled.Should().BeTrue();
        schedule.NextRunAt.Should().BeNull();
        schedule.LastRunAt.Should().BeNull();
        schedule.ConcurrencyToken.Should().Be(Guid.Empty);
    }
}
