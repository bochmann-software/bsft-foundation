using Bsft.Scheduling.StoredSchedules;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests.StoredSchedules;

public class InMemoryStoredScheduleStoreTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemoryStoredScheduleStore _store = new();

    private static StoredSchedule Schedule(string tenantId, string jobKey, DateTimeOffset? nextRunAt, bool enabled = true) => new()
    {
        TenantId = tenantId,
        JobKey = jobKey,
        Schedule = JobSchedule.Every(TimeSpan.FromMinutes(5)),
        Enabled = enabled,
        NextRunAt = nextRunAt,
        CreatedAt = _now,
        UpdatedAt = _now,
    };

    [Fact]
    public async Task CreateAssignsAnIdentityAndStampsItBackOntoTheCaller()
    {
        var schedule = Schedule("tenant-a", "report", _now);

        var id = await _store.CreateAsync(schedule);

        id.Should().BeGreaterThan(0);
        schedule.Id.Should().Be(id);
        schedule.ConcurrencyToken.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateRejectsNull()
    {
        var act = async () => await _store.CreateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("schedule");
    }

    [Fact]
    public async Task AReadHandsOutACopySoACallerCannotRewriteTheStore()
    {
        var id = await _store.CreateAsync(Schedule("tenant-a", "report", _now));

        var read = await _store.FindAsync(id);
        read!.JobKey = "tampered";

        var reread = await _store.FindAsync(id);
        reread!.JobKey.Should().Be("report");
    }

    [Fact]
    public async Task GetReturnsNullForAnUnknownIdentity()
    {
        (await _store.FindAsync(404)).Should().BeNull();
    }

    [Fact]
    public async Task GetByTenantFiltersToTheGivenTenant()
    {
        await _store.CreateAsync(Schedule("tenant-a", "report", _now));
        await _store.CreateAsync(Schedule("tenant-b", "cleanup", _now));

        var results = await _store.ListByTenantAsync("tenant-a");

        results.Should().ContainSingle(s => s.JobKey == "report");
    }

    [Fact]
    public async Task GetByTenantWithNullReturnsEveryTenant()
    {
        await _store.CreateAsync(Schedule("tenant-a", "report", _now));
        await _store.CreateAsync(Schedule("tenant-b", "cleanup", _now));

        var results = await _store.ListByTenantAsync(null);

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateRejectsNull()
    {
        var act = async () => await _store.TryUpdateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("schedule");
    }

    [Fact]
    public async Task UpdateReturnsFalseForAnUnknownIdentity()
    {
        var unknown = Schedule("tenant-a", "report", _now);
        unknown.Id = 404;

        (await _store.TryUpdateAsync(unknown)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateReplacesTheEditableFieldsAndMintsAFreshToken()
    {
        var id = await _store.CreateAsync(Schedule("tenant-a", "report", _now));
        var stored = (await _store.FindAsync(id))!;
        var originalToken = stored.ConcurrencyToken;

        stored.JobKey = "report-v2";
        stored.Schedule = JobSchedule.Cron("0 3 * * *");
        stored.Payload = "{}";
        stored.NextRunAt = _now.AddDays(1);
        stored.UpdatedAt = _now.AddMinutes(1);

        (await _store.TryUpdateAsync(stored)).Should().BeTrue();

        var reread = (await _store.FindAsync(id))!;
        reread.JobKey.Should().Be("report-v2");
        reread.Schedule.CronExpressionText.Should().Be("0 3 * * *");
        reread.Payload.Should().Be("{}");
        reread.NextRunAt.Should().Be(_now.AddDays(1));
        reread.ConcurrencyToken.Should().NotBe(originalToken);
    }

    [Fact]
    public async Task SetEnabledReturnsFalseForAnUnknownIdentity()
    {
        (await _store.TrySetEnabledAsync(404, false)).Should().BeFalse();
    }

    [Fact]
    public async Task SetEnabledTogglesTheFlag()
    {
        var id = await _store.CreateAsync(Schedule("tenant-a", "report", _now));

        (await _store.TrySetEnabledAsync(id, false)).Should().BeTrue();

        (await _store.FindAsync(id))!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteReturnsFalseForAnUnknownIdentity()
    {
        (await _store.TryDeleteAsync(404)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteRemovesTheSchedule()
    {
        var id = await _store.CreateAsync(Schedule("tenant-a", "report", _now));

        (await _store.TryDeleteAsync(id)).Should().BeTrue();
        (await _store.FindAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task GetDueOnlyReturnsEnabledSchedulesDueByNow()
    {
        var due = Schedule("tenant-a", "due", _now.AddMinutes(-1));
        var future = Schedule("tenant-a", "future", _now.AddMinutes(1));
        var disabled = Schedule("tenant-a", "disabled", _now.AddMinutes(-1), enabled: false);
        var noNext = Schedule("tenant-a", "no-next", null);

        await _store.CreateAsync(due);
        await _store.CreateAsync(future);
        await _store.CreateAsync(disabled);
        await _store.CreateAsync(noNext);

        var result = await _store.ListDueAsync(_now, 10);

        result.Should().ContainSingle(s => s.JobKey == "due");
    }

    [Fact]
    public async Task GetDueOrdersByNextRunAtThenId()
    {
        var later = await CreateDue("later", _now.AddMinutes(-1));
        var earlier = await CreateDue("earlier", _now.AddMinutes(-5));

        var result = await _store.ListDueAsync(_now, 10);

        result.Select(s => s.Id).Should().Equal(earlier, later);
    }

    [Fact]
    public async Task GetDueRespectsTheBatchSize()
    {
        await CreateDue("a", _now.AddMinutes(-1));
        await CreateDue("b", _now.AddMinutes(-1));

        var result = await _store.ListDueAsync(_now, 1);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task TryAdvanceRejectsNull()
    {
        var act = async () => await _store.AdvanceOrNullAsync(null!, _now, null);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("schedule");
    }

    [Fact]
    public async Task TryAdvanceReturnsNullForAnUnknownIdentity()
    {
        var unknown = Schedule("tenant-a", "report", _now);
        unknown.Id = 404;

        (await _store.AdvanceOrNullAsync(unknown, _now, null)).Should().BeNull();
    }

    [Fact]
    public async Task TryAdvanceReturnsNullWhenTheTokenNoLongerMatches()
    {
        var id = await _store.CreateAsync(Schedule("tenant-a", "report", _now));
        var stale = (await _store.FindAsync(id))!;

        // A concurrent advance moves the token forward before this caller gets to try.
        await _store.AdvanceOrNullAsync((await _store.FindAsync(id))!, _now, _now.AddMinutes(5));

        (await _store.AdvanceOrNullAsync(stale, _now, _now.AddMinutes(10))).Should().BeNull();
    }

    [Fact]
    public async Task TryAdvanceUpdatesTheDueSlotAndMintsAFreshToken()
    {
        var id = await _store.CreateAsync(Schedule("tenant-a", "report", _now));
        var claimed = (await _store.FindAsync(id))!;
        var originalToken = claimed.ConcurrencyToken;

        var advanced = await _store.AdvanceOrNullAsync(claimed, _now, _now.AddMinutes(5));

        advanced.Should().NotBeNull();
        advanced!.LastRunAt.Should().Be(_now);
        advanced.NextRunAt.Should().Be(_now.AddMinutes(5));
        advanced.ConcurrencyToken.Should().NotBe(originalToken);

        (await _store.FindAsync(id))!.NextRunAt.Should().Be(_now.AddMinutes(5));
    }

    [Fact]
    public async Task TryAdvanceAcceptsANullNextRunAtWhenTheScheduleHasNoFurtherOccurrence()
    {
        var id = await _store.CreateAsync(Schedule("tenant-a", "report", _now));
        var claimed = (await _store.FindAsync(id))!;

        var advanced = await _store.AdvanceOrNullAsync(claimed, _now, null);

        advanced!.NextRunAt.Should().BeNull();
    }

    private async Task<long> CreateDue(string jobKey, DateTimeOffset nextRunAt) =>
        await _store.CreateAsync(Schedule("tenant-a", jobKey, nextRunAt));
}
