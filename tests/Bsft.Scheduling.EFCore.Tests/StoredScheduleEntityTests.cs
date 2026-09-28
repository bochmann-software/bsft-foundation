using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests;

public class StoredScheduleEntityTests
{
    [Fact]
    public void ANewEntityStartsWithAnEmptyJobKeyAndNoOptionalValues()
    {
        var entity = new StoredScheduleEntity();

        entity.Id.Should().Be(0);
        entity.TenantId.Should().BeNull();
        entity.JobKey.Should().BeEmpty();
        entity.CronExpression.Should().BeNull();
        entity.IntervalTicks.Should().BeNull();
        entity.Payload.Should().BeNull();
        entity.Enabled.Should().BeFalse();
        entity.NextRunAt.Should().BeNull();
        entity.LastRunAt.Should().BeNull();
        entity.ConcurrencyToken.Should().BeEmpty();
    }

    [Fact]
    public void EveryPropertyRoundTripsTheValueItWasGiven()
    {
        var now = new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc);
        var token = Guid.NewGuid();

        var entity = new StoredScheduleEntity
        {
            Id = 7,
            TenantId = "tenant-a",
            JobKey = "report",
            CronExpression = "0 3 * * *",
            IntervalTicks = null,
            Payload = "{}",
            Enabled = true,
            NextRunAt = now.AddDays(1),
            LastRunAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyToken = token,
        };

        entity.Id.Should().Be(7);
        entity.TenantId.Should().Be("tenant-a");
        entity.JobKey.Should().Be("report");
        entity.CronExpression.Should().Be("0 3 * * *");
        entity.IntervalTicks.Should().BeNull();
        entity.Payload.Should().Be("{}");
        entity.Enabled.Should().BeTrue();
        entity.NextRunAt.Should().Be(now.AddDays(1));
        entity.LastRunAt.Should().Be(now);
        entity.CreatedAt.Should().Be(now);
        entity.UpdatedAt.Should().Be(now);
        entity.ConcurrencyToken.Should().Be(token);
    }
}
