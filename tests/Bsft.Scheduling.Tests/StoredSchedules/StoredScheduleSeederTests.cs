using Bsft.Scheduling.StoredSchedules;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Scheduling.Tests.StoredSchedules;

public class StoredScheduleSeederTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemoryStoredScheduleStore _store = new();
    private readonly FixedTimeProvider _time = new(_now);
    private readonly IJobScheduler _jobScheduler = Substitute.For<IJobScheduler>();

    private readonly BsftSchedulingOptions _options = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(1),
        StoredScheduleSeedInterval = TimeSpan.FromMinutes(5),
        StoredScheduleBatchSize = 50,
    };

    private ServiceProvider BuildScopeProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_jobScheduler);
        return services.BuildServiceProvider();
    }

    private StoredScheduleSeeder CreateSeeder(ServiceProvider provider, TimeProvider? time = null) =>
        new(_store, provider.GetRequiredService<IServiceScopeFactory>(), time ?? _time, Options.Create(_options), NullLogger<StoredScheduleSeeder>.Instance);

    [Fact]
    public void TheConstructorRejectsEveryMissingDependency()
    {
        using var provider = BuildScopeProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(_options);
        var logger = NullLogger<StoredScheduleSeeder>.Instance;

        ((Action)(() => new StoredScheduleSeeder(null!, scopeFactory, _time, options, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("store");
        ((Action)(() => new StoredScheduleSeeder(_store, null!, _time, options, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
        ((Action)(() => new StoredScheduleSeeder(_store, scopeFactory, null!, options, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        ((Action)(() => new StoredScheduleSeeder(_store, scopeFactory, _time, null!, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        ((Action)(() => new StoredScheduleSeeder(_store, scopeFactory, _time, options, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task AnAlreadyCancelledTokenSeedsNothing()
    {
        using var provider = BuildScopeProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await CreateSeeder(provider).SeedPeriodicallyAsync(cts.Token);

        await _jobScheduler.DidNotReceiveWithAnyArgs().EnqueueAtAsync(default!, default, default, default);
    }

    [Fact]
    public async Task ADueScheduleIsAdvancedAndEnqueuedAsAOneShotRun()
    {
        using var provider = BuildScopeProvider();
        var created = new StoredSchedule
        {
            TenantId = "tenant-a",
            JobKey = "report",
            Schedule = JobSchedule.Every(TimeSpan.FromMinutes(5)),
            Payload = "{\"orderId\":1}",
            Enabled = true,
            NextRunAt = _now.AddMinutes(-1),
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        await _store.CreateAsync(created);

        using var cts = new CancellationTokenSource();
        _jobScheduler
            .EnqueueAtAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return Task.FromResult(1L);
            });

        await CreateSeeder(provider).SeedPeriodicallyAsync(cts.Token);

        await _jobScheduler.Received(1).EnqueueAtAsync("report", _now.AddMinutes(-1), "{\"orderId\":1}", Arg.Any<CancellationToken>());

        var advanced = await _store.FindAsync(created.Id);
        advanced!.LastRunAt.Should().Be(_now);
        // The due instant (now - 1 minute) is behind the current clock, so the next occurrence is
        // anchored on now rather than replaying from the stale due instant.
        advanced.NextRunAt.Should().Be(_now.AddMinutes(5));
    }

    [Fact]
    public async Task NoDueScheduleMeansNothingIsEnqueued()
    {
        using var provider = BuildScopeProvider();
        await _store.CreateAsync(new StoredSchedule
        {
            JobKey = "report",
            Schedule = JobSchedule.Every(TimeSpan.FromMinutes(5)),
            Enabled = true,
            NextRunAt = _now.AddMinutes(1),
            CreatedAt = _now,
            UpdatedAt = _now,
        });

        using var cts = new CancellationTokenSource();
        var seeder = CreateSeeder(provider, new StopAtTheFirstWait(_now, cts));

        await seeder.Invoking(s => s.SeedPeriodicallyAsync(cts.Token)).Should().NotThrowAsync();

        await _jobScheduler.DidNotReceiveWithAnyArgs().EnqueueAtAsync(default!, default, default, default);
    }

    [Fact]
    public async Task ASecondInstanceLosingTheAdvanceRaceEnqueuesNothing()
    {
        using var provider = BuildScopeProvider();
        var created = new StoredSchedule
        {
            JobKey = "report",
            Schedule = JobSchedule.Every(TimeSpan.FromMinutes(5)),
            Enabled = true,
            NextRunAt = _now.AddMinutes(-1),
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        await _store.CreateAsync(created);

        // Another instance claims the slot first, between ListDueAsync and AdvanceOrNullAsync: this one
        // read the schedule as due, and its advance finds the slot already taken.
        var racing = new AdvancedByAnotherInstanceStore(_store, _now.AddMinutes(4));
        using var cts = new CancellationTokenSource();
        var seeder = new StoredScheduleSeeder(
            racing,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StopAtTheFirstWait(_now, cts),
            Options.Create(_options),
            NullLogger<StoredScheduleSeeder>.Instance);

        await seeder.SeedPeriodicallyAsync(cts.Token);

        racing.ReadAsDue.Should().ContainSingle().Which.Id.Should().Be(created.Id);
        (await _store.FindAsync(created.Id))!.NextRunAt.Should().Be(_now.AddMinutes(4));
        await _jobScheduler.DidNotReceiveWithAnyArgs().EnqueueAtAsync(default!, default, default, default);
    }

    [Fact]
    public async Task APassCancelledByTheHostStopsTheLoopWithoutReportingAFailure()
    {
        using var provider = BuildScopeProvider();
        using var cts = new CancellationTokenSource();
        var store = Substitute.For<IStoredScheduleStore>();
        store.ListDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<StoredSchedule>>>(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });
        var time = Substitute.ForPartsOf<TimeProvider>();
        var seeder = new StoredScheduleSeeder(
            store,
            provider.GetRequiredService<IServiceScopeFactory>(),
            time,
            Options.Create(_options),
            NullLogger<StoredScheduleSeeder>.Instance);

        await seeder.SeedPeriodicallyAsync(cts.Token);

        await store.ReceivedWithAnyArgs(1).ListDueAsync(default, default, default);
        time.DidNotReceiveWithAnyArgs().CreateTimer(default!, default, default, default);
    }

    [Fact]
    public async Task AScheduleWithNoFurtherOccurrenceIsAdvancedToNullAndStillEnqueuedOnce()
    {
        using var provider = BuildScopeProvider();
        // "0 0 30 2 *" never matches: February never has a 30th.
        var created = new StoredSchedule
        {
            JobKey = "report",
            Schedule = JobSchedule.Cron("0 0 30 2 *"),
            Enabled = true,
            NextRunAt = _now.AddMinutes(-1),
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        await _store.CreateAsync(created);

        using var cts = new CancellationTokenSource();
        _jobScheduler
            .EnqueueAtAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return Task.FromResult(1L);
            });

        await CreateSeeder(provider).SeedPeriodicallyAsync(cts.Token);

        (await _store.FindAsync(created.Id))!.NextRunAt.Should().BeNull();
        await _jobScheduler.Received(1).EnqueueAtAsync("report", Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedingIsRetriedAfterAStoreFailure()
    {
        using var provider = BuildScopeProvider();
        var failingStore = new ThrowOnceThenEmptyStore();
        var seeder = new StoredScheduleSeeder(
            failingStore,
            provider.GetRequiredService<IServiceScopeFactory>(),
            _time,
            Options.Create(_options),
            NullLogger<StoredScheduleSeeder>.Instance);

        using var cts = new CancellationTokenSource();
        failingStore.CancelAfterSecondCall = cts;

        await seeder.SeedPeriodicallyAsync(cts.Token);

        failingStore.Calls.Should().Be(2);
    }

    [Fact]
    public async Task TheHostedServiceSeedsAndStopsCleanly()
    {
        using var provider = BuildScopeProvider();
        await _store.CreateAsync(new StoredSchedule
        {
            TenantId = "tenant-a",
            JobKey = "report",
            Schedule = JobSchedule.Every(TimeSpan.FromMinutes(5)),
            Enabled = true,
            NextRunAt = _now.AddMinutes(-1),
            CreatedAt = _now,
            UpdatedAt = _now,
        });
        var seeder = CreateSeeder(provider);

        await seeder.StartAsync(CancellationToken.None);
        // Since Microsoft.Extensions.Hosting 10, StartAsync runs the whole of ExecuteAsync on the
        // thread pool, so a stop right after the start can come before the first seeding. The test
        // waits for the due schedule to be enqueued rather than relying on the synchronous start of
        // earlier versions.
        SpinWait.SpinUntil(() => _jobScheduler.ReceivedCalls().Any(), TimeSpan.FromSeconds(10)).Should().BeTrue();
        await seeder.StopAsync(CancellationToken.None);

        seeder.Dispose();
    }

    /// <summary>
    /// A clock that stops the seeder once its first pass is over: the seeder asks for a timer only to wait
    /// out the interval after a pass, so that request is the point the pass has provably finished.
    /// </summary>
    /// <remarks>
    /// A stop driven by a timer of the test's own either lands before the first pass, which makes the
    /// assertion that nothing was enqueued pass without anything having been seeded, or not at all on a
    /// loaded machine. Stopping at the wait asserts against a pass that ran.
    /// </remarks>
    private sealed class StopAtTheFirstWait(DateTimeOffset now, CancellationTokenSource stopping) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            stopping.Cancel();

            return System.CreateTimer(callback, state, dueTime, period);
        }
    }

    /// <summary>
    /// A store that hands out what is due and lets another instance advance every one of those schedules
    /// before the caller gets to, which is the race the advance is conditional for.
    /// </summary>
    private sealed class AdvancedByAnotherInstanceStore(IStoredScheduleStore inner, DateTimeOffset advancedTo)
        : IStoredScheduleStore
    {
        public List<StoredSchedule> ReadAsDue { get; } = [];

        public Task<long> CreateAsync(StoredSchedule schedule, CancellationToken ct = default) => inner.CreateAsync(schedule, ct);

        public Task<StoredSchedule?> FindAsync(long id, CancellationToken ct = default) => inner.FindAsync(id, ct);

        public Task<IReadOnlyList<StoredSchedule>> ListByTenantAsync(string? tenantId, CancellationToken ct = default) =>
            inner.ListByTenantAsync(tenantId, ct);

        public Task<bool> TryUpdateAsync(StoredSchedule schedule, CancellationToken ct = default) => inner.TryUpdateAsync(schedule, ct);

        public Task<bool> TrySetEnabledAsync(long id, bool enabled, CancellationToken ct = default) =>
            inner.TrySetEnabledAsync(id, enabled, ct);

        public Task<bool> TryDeleteAsync(long id, CancellationToken ct = default) => inner.TryDeleteAsync(id, ct);

        public async Task<IReadOnlyList<StoredSchedule>> ListDueAsync(DateTimeOffset now, int batchSize, CancellationToken ct = default)
        {
            var due = await inner.ListDueAsync(now, batchSize, ct);

            foreach (var schedule in due)
            {
                ReadAsDue.Add(schedule);
                (await inner.AdvanceOrNullAsync(schedule, now, advancedTo, ct)).Should().NotBeNull();
            }

            return due;
        }

        public Task<StoredSchedule?> AdvanceOrNullAsync(
            StoredSchedule schedule, DateTimeOffset ranAt, DateTimeOffset? nextRunAt, CancellationToken ct = default) =>
            inner.AdvanceOrNullAsync(schedule, ranAt, nextRunAt, ct);
    }

    /// <summary>A store whose first <see cref="ListDueAsync"/> call throws and whose second succeeds empty.</summary>
    private sealed class ThrowOnceThenEmptyStore : IStoredScheduleStore
    {
        public int Calls { get; private set; }

        public CancellationTokenSource? CancelAfterSecondCall { get; set; }

        public Task<long> CreateAsync(StoredSchedule schedule, CancellationToken ct = default) => Task.FromResult(1L);

        public Task<StoredSchedule?> FindAsync(long id, CancellationToken ct = default) => Task.FromResult<StoredSchedule?>(null);

        public Task<IReadOnlyList<StoredSchedule>> ListByTenantAsync(string? tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StoredSchedule>>([]);

        public Task<bool> TryUpdateAsync(StoredSchedule schedule, CancellationToken ct = default) => Task.FromResult(false);

        public Task<bool> TrySetEnabledAsync(long id, bool enabled, CancellationToken ct = default) => Task.FromResult(false);

        public Task<bool> TryDeleteAsync(long id, CancellationToken ct = default) => Task.FromResult(false);

        public Task<IReadOnlyList<StoredSchedule>> ListDueAsync(DateTimeOffset now, int batchSize, CancellationToken ct = default)
        {
            Calls++;

            if (Calls == 1)
                throw new InvalidOperationException("the store is unreachable");

            CancelAfterSecondCall?.Cancel();
            return Task.FromResult<IReadOnlyList<StoredSchedule>>([]);
        }

        public Task<StoredSchedule?> AdvanceOrNullAsync(
            StoredSchedule schedule, DateTimeOffset ranAt, DateTimeOffset? nextRunAt, CancellationToken ct = default) =>
            Task.FromResult<StoredSchedule?>(null);
    }
}
