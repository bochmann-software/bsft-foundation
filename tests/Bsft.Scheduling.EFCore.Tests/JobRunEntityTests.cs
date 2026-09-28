using Bsft.Scheduling.EFCore.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests;

public class JobRunEntityTests
{
    [Fact]
    public void ANewEntityStartsWithAnEmptyJobNameAndNoOptionalValues()
    {
        var entity = new JobRunEntity();

        entity.Id.Should().Be(0);
        entity.JobName.Should().BeEmpty();
        entity.Status.Should().Be(JobRunStatus.Pending);
        entity.IsRecurring.Should().BeFalse();
        entity.Payload.Should().BeNull();
        entity.CorrelationId.Should().BeNull();
        entity.AttemptCount.Should().Be(0);
        entity.LeaseOwner.Should().BeNull();
        entity.LeaseExpiresAt.Should().BeNull();
        entity.SettledAt.Should().BeNull();
        entity.ConcurrencyToken.Should().BeEmpty();
    }

    [Fact]
    public void EveryPropertyRoundTripsTheValueItWasGiven()
    {
        var due = new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc);
        var token = Guid.NewGuid();

        var entity = new JobRunEntity
        {
            Id = 7,
            JobName = "report",
            DueAt = due,
            Status = JobRunStatus.Running,
            IsRecurring = true,
            Payload = "{}",
            CorrelationId = "corr-9",
            AttemptCount = 2,
            LeaseOwner = "instance-1",
            LeaseExpiresAt = due.AddMinutes(5),
            SettledAt = due.AddMinutes(7),
            ConcurrencyToken = token,
        };

        entity.Id.Should().Be(7);
        entity.JobName.Should().Be("report");
        entity.DueAt.Should().Be(due);
        entity.Status.Should().Be(JobRunStatus.Running);
        entity.IsRecurring.Should().BeTrue();
        entity.Payload.Should().Be("{}");
        entity.CorrelationId.Should().Be("corr-9");
        entity.AttemptCount.Should().Be(2);
        entity.LeaseOwner.Should().Be("instance-1");
        entity.LeaseExpiresAt.Should().Be(due.AddMinutes(5));
        entity.SettledAt.Should().Be(due.AddMinutes(7));
        entity.ConcurrencyToken.Should().Be(token);
    }
}
