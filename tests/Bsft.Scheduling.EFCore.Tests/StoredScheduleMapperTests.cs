using Bsft.Scheduling.StoredSchedules;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests;

public class StoredScheduleMapperTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly StoredScheduleMapper _mapper = new();

    [Fact]
    public void TheMapperRejectsMissingArguments()
    {
        ((Action)(() => _mapper.ToEntity(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("schedule");
        ((Action)(() => _mapper.ToSchedule(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("entity");
    }

    [Fact]
    public void AnIntervalScheduleRoundTripsThroughTheEntity()
    {
        var schedule = new StoredSchedule
        {
            Id = 7,
            TenantId = "tenant-a",
            JobKey = "report",
            Schedule = JobSchedule.Every(TimeSpan.FromMinutes(5)),
            Payload = "{}",
            Enabled = true,
            NextRunAt = _now.AddMinutes(5),
            LastRunAt = _now,
            CreatedAt = _now,
            UpdatedAt = _now,
            ConcurrencyToken = Guid.NewGuid(),
        };

        var entity = _mapper.ToEntity(schedule);
        entity.CronExpression.Should().BeNull();
        entity.IntervalTicks.Should().Be(TimeSpan.FromMinutes(5).Ticks);

        var roundTripped = _mapper.ToSchedule(entity);
        roundTripped.TenantId.Should().Be("tenant-a");
        roundTripped.JobKey.Should().Be("report");
        roundTripped.Schedule.Interval.Should().Be(TimeSpan.FromMinutes(5));
        roundTripped.Payload.Should().Be("{}");
        roundTripped.Enabled.Should().BeTrue();
        roundTripped.NextRunAt.Should().Be(_now.AddMinutes(5));
        roundTripped.LastRunAt.Should().Be(_now);
        roundTripped.CreatedAt.Should().Be(_now);
        roundTripped.UpdatedAt.Should().Be(_now);
        roundTripped.ConcurrencyToken.Should().Be(schedule.ConcurrencyToken);
    }

    [Fact]
    public void ACronScheduleRoundTripsThroughTheEntity()
    {
        var schedule = new StoredSchedule
        {
            JobKey = "report",
            Schedule = JobSchedule.Cron("0 3 * * *"),
            Enabled = true,
            CreatedAt = _now,
            UpdatedAt = _now,
        };

        var entity = _mapper.ToEntity(schedule);
        entity.CronExpression.Should().Be("0 3 * * *");
        entity.IntervalTicks.Should().BeNull();

        var roundTripped = _mapper.ToSchedule(entity);
        roundTripped.Schedule.CronExpressionText.Should().Be("0 3 * * *");
    }

    [Fact]
    public void ANullNextAndLastRunAtRoundTripAsNull()
    {
        var entity = _mapper.ToEntity(new StoredSchedule
        {
            JobKey = "report",
            Schedule = JobSchedule.Every(TimeSpan.FromMinutes(5)),
            CreatedAt = _now,
            UpdatedAt = _now,
        });

        entity.NextRunAt.Should().BeNull();
        entity.LastRunAt.Should().BeNull();

        var roundTripped = _mapper.ToSchedule(entity);
        roundTripped.NextRunAt.Should().BeNull();
        roundTripped.LastRunAt.Should().BeNull();
    }
}
