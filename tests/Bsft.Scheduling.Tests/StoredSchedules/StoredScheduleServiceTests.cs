using Bsft.Scheduling.StoredSchedules;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests.StoredSchedules;

public class StoredScheduleServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemoryStoredScheduleStore _store = new();
    private readonly FixedTimeProvider _time = new(_now);

    private readonly JobRegistry _registry = new(
        [new JobDefinition("report", typeof(RecordingJob), null, JobOverlapPolicy.Forbid)],
        JobOptionsMonitor.Defaults);

    private StoredScheduleService CreateService() => new(_store, _registry, _time);

    [Fact]
    public void TheConstructorRejectsEveryMissingDependency()
    {
        ((Action)(() => new StoredScheduleService(null!, _registry, _time)))
            .Should().Throw<ArgumentNullException>().WithParameterName("store");
        ((Action)(() => new StoredScheduleService(_store, null!, _time)))
            .Should().Throw<ArgumentNullException>().WithParameterName("registry");
        ((Action)(() => new StoredScheduleService(_store, _registry, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    // ---- CreateAsync ----------------------------------------------------------------------------

    [Fact]
    public async Task CreateRejectsNullArguments()
    {
        var service = CreateService();
        var schedule = JobSchedule.Every(TimeSpan.FromMinutes(5));

        (await ((Func<Task>)(() => service.CreateAsync("tenant-a", null!, schedule, null))).Should().ThrowAsync<ArgumentNullException>())
            .WithParameterName("jobKey");
        (await ((Func<Task>)(() => service.CreateAsync("tenant-a", "report", null!, null))).Should().ThrowAsync<ArgumentNullException>())
            .WithParameterName("schedule");
    }

    [Fact]
    public async Task CreateRejectsAJobKeyThatIsNotRegistered()
    {
        var service = CreateService();

        var act = async () => await service.CreateAsync("tenant-a", "unknown", JobSchedule.Every(TimeSpan.FromMinutes(5)), null);

        await act.Should().ThrowAsync<JobNotRegisteredException>();
    }

    [Fact]
    public async Task CreateComputesTheFirstOccurrenceAndPersistsTheSchedule()
    {
        var service = CreateService();
        var rule = JobSchedule.Every(TimeSpan.FromMinutes(5));

        var created = await service.CreateAsync("tenant-a", "report", rule, "{\"orderId\":1}");

        created.Id.Should().BeGreaterThan(0);
        created.TenantId.Should().Be("tenant-a");
        created.Enabled.Should().BeTrue();
        created.NextRunAt.Should().Be(_now.AddMinutes(5));
        created.Payload.Should().Be("{\"orderId\":1}");

        (await _store.FindAsync(created.Id))!.JobKey.Should().Be("report");
    }

    // ---- GetAsync / ListAsync --------------------------------------------------------------------

    [Fact]
    public async Task GetReadsThroughToTheStore()
    {
        var service = CreateService();
        var created = await service.CreateAsync("tenant-a", "report", JobSchedule.Every(TimeSpan.FromMinutes(5)), null);

        (await service.FindAsync("tenant-a", created.Id))!.Id.Should().Be(created.Id);
        (await service.FindAsync("tenant-a", 404)).Should().BeNull();
        (await service.FindAsync("tenant-b", created.Id)).Should().BeNull();
    }

    [Fact]
    public async Task ListReadsThroughToTheStore()
    {
        var service = CreateService();
        await service.CreateAsync("tenant-a", "report", JobSchedule.Every(TimeSpan.FromMinutes(5)), null);
        await service.CreateAsync("tenant-b", "report", JobSchedule.Every(TimeSpan.FromMinutes(5)), null);

        (await service.ListAsync("tenant-a")).Should().ContainSingle();
        (await service.ListAsync(null)).Should().HaveCount(2);
    }

    // ---- UpdateAsync ------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateRejectsNull()
    {
        var service = CreateService();

        var act = async () => await service.TryUpdateAsync("tenant-a", 1, null!, null);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("schedule");
    }

    [Fact]
    public async Task UpdateReturnsFalseForAnUnknownIdentity()
    {
        var service = CreateService();

        (await service.TryUpdateAsync("tenant-a", 404, JobSchedule.Every(TimeSpan.FromMinutes(5)), null)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateReplacesTheRuleAndRecomputesTheNextOccurrenceFromNow()
    {
        var service = CreateService();
        var created = await service.CreateAsync("tenant-a", "report", JobSchedule.Every(TimeSpan.FromMinutes(5)), null);

        _time.Advance(TimeSpan.FromMinutes(2));
        var newRule = JobSchedule.Every(TimeSpan.FromHours(1));

        (await service.TryUpdateAsync("tenant-a", created.Id, newRule, "{}")).Should().BeTrue();

        var updated = (await service.FindAsync("tenant-a", created.Id))!;
        updated.NextRunAt.Should().Be(_time.GetUtcNow().AddHours(1));
        updated.Payload.Should().Be("{}");
    }

    // ---- SetEnabledAsync / DeleteAsync --------------------------------------------------------------

    [Fact]
    public async Task SetEnabledReadsThroughToTheStore()
    {
        var service = CreateService();
        var created = await service.CreateAsync("tenant-a", "report", JobSchedule.Every(TimeSpan.FromMinutes(5)), null);

        (await service.TrySetEnabledAsync("tenant-a", created.Id, false)).Should().BeTrue();
        (await service.FindAsync("tenant-a", created.Id))!.Enabled.Should().BeFalse();

        (await service.TrySetEnabledAsync("tenant-a", 404, false)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteReadsThroughToTheStore()
    {
        var service = CreateService();
        var created = await service.CreateAsync("tenant-a", "report", JobSchedule.Every(TimeSpan.FromMinutes(5)), null);

        (await service.TryDeleteAsync("tenant-a", created.Id)).Should().BeTrue();
        (await service.FindAsync("tenant-a", created.Id)).Should().BeNull();

        (await service.TryDeleteAsync("tenant-a", 404)).Should().BeFalse();
    }

    // ---- Tenant scoping ---------------------------------------------------------------------------

    [Fact]
    public async Task TryUpdate_ForAScheduleOfAnotherTenant_ChangesNothing()
    {
        var service = CreateService();
        var other = await service.CreateAsync("tenant-b", "report", JobSchedule.Every(TimeSpan.FromMinutes(5)), "kept");

        var changed = await service.TryUpdateAsync("tenant-a", other.Id, JobSchedule.Every(TimeSpan.FromHours(1)), "{}");

        changed.Should().BeFalse();
        (await _store.FindAsync(other.Id))!.Payload.Should().Be("kept");
    }

    [Fact]
    public async Task TrySetEnabled_ForAScheduleOfAnotherTenant_ChangesNothing()
    {
        var service = CreateService();
        var other = await service.CreateAsync("tenant-b", "report", JobSchedule.Every(TimeSpan.FromMinutes(5)), null);

        (await service.TrySetEnabledAsync("tenant-a", other.Id, false)).Should().BeFalse();
        (await _store.FindAsync(other.Id))!.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task TryDelete_ForAScheduleOfAnotherTenant_RemovesNothing()
    {
        var service = CreateService();
        var other = await service.CreateAsync("tenant-b", "report", JobSchedule.Every(TimeSpan.FromMinutes(5)), null);

        (await service.TryDeleteAsync("tenant-a", other.Id)).Should().BeFalse();
        (await _store.FindAsync(other.Id)).Should().NotBeNull();
    }

    /// <summary>A schedule that belongs to no tenant is reached by naming no tenant, not by naming any.</summary>
    [Fact]
    public async Task TryDelete_ForATenantlessSchedule_NeedsTheTenantlessCaller()
    {
        var service = CreateService();
        var shared = await service.CreateAsync(null, "report", JobSchedule.Every(TimeSpan.FromMinutes(5)), null);

        (await service.TryDeleteAsync("tenant-a", shared.Id)).Should().BeFalse();
        (await service.TryDeleteAsync(null, shared.Id)).Should().BeTrue();
    }
}
