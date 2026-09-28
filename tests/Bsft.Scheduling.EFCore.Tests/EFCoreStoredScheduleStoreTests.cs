using Bsft.Scheduling.EFCore.Tests.Support;
using Bsft.Scheduling.StoredSchedules;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests;

/// <summary>
/// Stored-schedule persistence against SQLite over a single shared open connection: the EF InMemory
/// provider ignores both the concurrency token and the filtered indexes the store's correctness rests
/// on, matching the reasoning in <see cref="EFCoreJobStoreTests"/>.
/// </summary>
public class EFCoreStoredScheduleStoreTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly EFCoreStoredScheduleStore _store;

    public EFCoreStoredScheduleStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestStoredScheduleDbContext>(opt => opt.UseSqlite(_connection));
        services.AddScoped<IStoredScheduleDbContext>(sp => sp.GetRequiredService<TestStoredScheduleDbContext>());
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<TestStoredScheduleDbContext>().Database.EnsureCreated();

        _store = new EFCoreStoredScheduleStore(_provider.GetRequiredService<IServiceScopeFactory>(), new StoredScheduleMapper());
    }

    private static StoredSchedule Schedule(string? tenantId, string jobKey, DateTimeOffset? nextRunAt, bool enabled = true) => new()
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
    public void TheConstructorRejectsEveryMissingDependency()
    {
        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();

        ((Action)(() => new EFCoreStoredScheduleStore(null!, new StoredScheduleMapper())))
            .Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
        ((Action)(() => new EFCoreStoredScheduleStore(scopeFactory, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("mapper");
    }

    // ---- CreateAsync ----------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsyncStoresTheScheduleAndStampsTheIdentityBackOntoTheCaller()
    {
        var schedule = Schedule("tenant-a", "report", _now);

        var id = await _store.CreateAsync(schedule);

        id.Should().BeGreaterThan(0);
        schedule.Id.Should().Be(id);
        schedule.ConcurrencyToken.Should().NotBeEmpty();

        var stored = await _store.FindAsync(id);
        stored!.JobKey.Should().Be("report");
    }

    [Fact]
    public async Task CreateAsyncRejectsNull()
    {
        var act = async () => await _store.CreateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("schedule");
    }

    // ---- GetAsync / ListByTenantAsync ---------------------------------------------------------------

    [Fact]
    public async Task GetAsyncReturnsNullForAnUnknownIdentity()
    {
        (await _store.FindAsync(404)).Should().BeNull();
    }

    [Fact]
    public async Task GetByTenantAsyncFiltersByTenant()
    {
        await _store.CreateAsync(Schedule("tenant-a", "report", _now));
        await _store.CreateAsync(Schedule("tenant-b", "cleanup", _now));

        (await _store.ListByTenantAsync("tenant-a")).Should().ContainSingle(s => s.JobKey == "report");
        (await _store.ListByTenantAsync(null)).Should().HaveCount(2);
    }

    // ---- UpdateAsync --------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsyncRejectsNull()
    {
        var act = async () => await _store.TryUpdateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("schedule");
    }

    [Fact]
    public async Task UpdateAsyncReturnsFalseForAnUnknownIdentity()
    {
        var unknown = Schedule("tenant-a", "report", _now);
        unknown.Id = 404;

        (await _store.TryUpdateAsync(unknown)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsyncReplacesTheEditableFields()
    {
        var id = await _store.CreateAsync(Schedule("tenant-a", "report", _now));
        var toUpdate = (await _store.FindAsync(id))!;
        toUpdate.JobKey = "report-v2";
        toUpdate.Schedule = JobSchedule.Cron("0 3 * * *");
        toUpdate.Payload = "{}";
        toUpdate.NextRunAt = _now.AddDays(1);
        toUpdate.UpdatedAt = _now.AddMinutes(1);

        (await _store.TryUpdateAsync(toUpdate)).Should().BeTrue();

        var reread = (await _store.FindAsync(id))!;
        reread.JobKey.Should().Be("report-v2");
        reread.Schedule.CronExpressionText.Should().Be("0 3 * * *");
        reread.Payload.Should().Be("{}");
        reread.NextRunAt.Should().Be(_now.AddDays(1));
    }

    // ---- SetEnabledAsync / DeleteAsync -----------------------------------------------------------

    [Fact]
    public async Task SetEnabledAsyncReturnsFalseForAnUnknownIdentity()
    {
        (await _store.TrySetEnabledAsync(404, false)).Should().BeFalse();
    }

    [Fact]
    public async Task SetEnabledAsyncTogglesTheFlag()
    {
        var id = await _store.CreateAsync(Schedule("tenant-a", "report", _now));

        (await _store.TrySetEnabledAsync(id, false)).Should().BeTrue();

        (await _store.FindAsync(id))!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsyncReturnsFalseForAnUnknownIdentity()
    {
        (await _store.TryDeleteAsync(404)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsyncRemovesTheSchedule()
    {
        var id = await _store.CreateAsync(Schedule("tenant-a", "report", _now));

        (await _store.TryDeleteAsync(id)).Should().BeTrue();
        (await _store.FindAsync(id)).Should().BeNull();
    }

    // ---- ListDueAsync --------------------------------------------------------------------------------

    [Fact]
    public async Task GetDueAsyncOnlyReturnsEnabledSchedulesDueByNow()
    {
        await _store.CreateAsync(Schedule("tenant-a", "due", _now.AddMinutes(-1)));
        await _store.CreateAsync(Schedule("tenant-a", "future", _now.AddMinutes(1)));
        await _store.CreateAsync(Schedule("tenant-a", "disabled", _now.AddMinutes(-1), enabled: false));
        await _store.CreateAsync(Schedule("tenant-a", "no-next", null));

        var result = await _store.ListDueAsync(_now, 10);

        result.Should().ContainSingle(s => s.JobKey == "due");
    }

    [Fact]
    public async Task GetDueAsyncRespectsTheBatchSize()
    {
        await _store.CreateAsync(Schedule("tenant-a", "a", _now.AddMinutes(-1)));
        await _store.CreateAsync(Schedule("tenant-a", "b", _now.AddMinutes(-1)));

        (await _store.ListDueAsync(_now, 1)).Should().HaveCount(1);
    }

    // ---- AdvanceOrNullAsync ----------------------------------------------------------------------------

    [Fact]
    public async Task AdvanceOrNullAsyncRejectsNull()
    {
        var act = async () => await _store.AdvanceOrNullAsync(null!, _now, null);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("schedule");
    }

    [Fact]
    public async Task AdvanceOrNullAsyncReturnsNullWhenTheTokenNoLongerMatches()
    {
        var id = await _store.CreateAsync(Schedule("tenant-a", "report", _now));
        var stale = (await _store.FindAsync(id))!;

        // A concurrent instance advances the slot first.
        await _store.AdvanceOrNullAsync((await _store.FindAsync(id))!, _now, _now.AddMinutes(5));

        (await _store.AdvanceOrNullAsync(stale, _now, _now.AddMinutes(10))).Should().BeNull();
    }

    [Fact]
    public async Task AdvanceOrNullAsyncReturnsNullForAnUnknownIdentity()
    {
        var unknown = Schedule("tenant-a", "report", _now);
        unknown.Id = 404;
        unknown.ConcurrencyToken = Guid.NewGuid();

        (await _store.AdvanceOrNullAsync(unknown, _now, null)).Should().BeNull();
    }

    [Fact]
    public async Task AdvanceOrNullAsyncUpdatesTheDueSlotAndMintsAFreshToken()
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
    public async Task AdvanceOrNullAsyncAcceptsANullNextRunAt()
    {
        var id = await _store.CreateAsync(Schedule("tenant-a", "report", _now));
        var claimed = (await _store.FindAsync(id))!;

        var advanced = await _store.AdvanceOrNullAsync(claimed, _now, null);

        advanced!.NextRunAt.Should().BeNull();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
