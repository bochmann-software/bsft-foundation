using System.Globalization;
using Bsft.Scheduling.EFCore.Tests.Fakes;
using Bsft.Scheduling.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests;

/// <summary>
/// What the job-run table deletes once a host says how long a settled run is worth keeping.
/// </summary>
public sealed class JobRunRetentionPassTests : IDisposable
{
    private const int BatchSize = 500;

    private static readonly DateTimeOffset _now = new(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _cutoff = _now - TimeSpan.FromDays(7);

    private readonly RecordingCommandInterceptor _commands = new();
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public JobRunRetentionPassTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestJobDbContext>(opt => opt.UseSqlite(_connection).AddInterceptors(_commands));
        services.AddScoped<IJobDbContext>(sp => sp.GetRequiredService<TestJobDbContext>());
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestJobDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void TheConstructorRejectsEveryMissingDependency()
    {
        var db = new TestJobDbContext(new DbContextOptionsBuilder<TestJobDbContext>().UseSqlite(_connection).Options);
        var options = Options.Create(new BsftSchedulingOptions());

        ((Action)(() => new JobRunRetentionPass(null!, options)))
            .Should().Throw<ArgumentNullException>().WithParameterName("db");
        ((Action)(() => new JobRunRetentionPass(db, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task OnlyTheRunsThatSettledBeforeTheCutoffArePurged()
    {
        await StoreAsync("old", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(30));
        await StoreAsync("recent", JobRunStatus.Completed, settledAgo: TimeSpan.FromHours(1));

        var purged = await PurgeAsync();

        purged.Should().Be(1);
        (await ReadAsync()).Should().ContainSingle().Which.JobName.Should().Be("recent");
    }

    [Theory]
    [InlineData(JobRunStatus.Completed)]
    [InlineData(JobRunStatus.Failed)]
    [InlineData(JobRunStatus.Skipped)]
    public async Task EverySettledRunIsPurged(JobRunStatus status)
    {
        await StoreAsync("job", status, settledAgo: TimeSpan.FromDays(30));

        (await PurgeAsync()).Should().Be(1);
        (await ReadAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(JobRunStatus.Pending)]
    [InlineData(JobRunStatus.Running)]
    public async Task ARunThatHasNotSettledIsNeverPurgedHoweverOldItIs(JobRunStatus status)
    {
        await StoreAsync("job", status, settledAgo: null, dueAgo: TimeSpan.FromDays(90));

        (await PurgeAsync()).Should().Be(0);
        (await PurgeAsync(keepLatest: 1)).Should().Be(0);
        (await ReadAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ARunThatWasDueLongAgoButSettledRecentlyIsKept()
    {
        await StoreAsync("job", JobRunStatus.Completed, settledAgo: TimeSpan.FromHours(1), dueAgo: TimeSpan.FromDays(30));

        (await PurgeAsync()).Should().Be(0);
        (await ReadAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task TheLatestSettledRunsOfEachJobAreKeptWhateverTheirAge()
    {
        await StoreAsync("a", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(40));
        await StoreAsync("a", JobRunStatus.Failed, settledAgo: TimeSpan.FromDays(30));
        await StoreAsync("a", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(20));
        await StoreAsync("b", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(40));
        await StoreAsync("b", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(30));

        var purged = await PurgeAsync(keepLatest: 2);

        purged.Should().Be(1);
        var left = await ReadAsync();
        left.Should().HaveCount(4);
        left.Count(e => e.JobName == "a").Should().Be(2);
        left.Count(e => e.JobName == "b").Should().Be(2);
        left.Where(e => e.JobName == "a").Should().NotContain(e => e.Status == JobRunStatus.Completed && e.SettledAt < (_now - TimeSpan.FromDays(35)).UtcDateTime);
    }

    [Fact]
    public async Task RunsThatSettledInTheSameInstantAreRankedByIdentity()
    {
        for (var index = 0; index < 3; index++)
            await StoreAsync("a", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(30));

        var ids = (await ReadAsync()).Select(e => e.Id).Order().ToList();

        (await PurgeAsync(keepLatest: 1)).Should().Be(2);
        (await ReadAsync()).Should().ContainSingle().Which.Id.Should().Be(ids[^1]);
    }

    [Fact]
    public async Task APendingRunDoesNotCountTowardsTheRunsKept()
    {
        await StoreAsync("a", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(30));
        await StoreAsync("a", JobRunStatus.Pending, settledAgo: null);

        (await PurgeAsync(keepLatest: 1)).Should().Be(0);
        (await ReadAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task EachJobsBoundaryIsComputedOnceWhateverTheNumberOfExpiredRuns()
    {
        string[] jobs = ["a", "b", "c", "d"];
        const int perJob = 40;
        const int keep = 5;

        foreach (var job in jobs)
            await ManyAsync(job, perJob);

        var before = await ReadAsync();
        _commands.Clear();
        var purged = await PurgeAsync(keepLatest: keep);
        var issued = _commands.Commands;

        purged.Should().Be(jobs.Length * (perJob - keep));
        var left = await ReadAsync();
        left.Should().HaveCount(jobs.Length * keep);

        foreach (var job in jobs)
        {
            var expected = before.Where(e => e.JobName == job)
                .OrderByDescending(e => e.SettledAt).ThenByDescending(e => e.Id)
                .Take(keep).Select(e => e.Id).Order();

            left.Where(e => e.JobName == job).Select(e => e.Id).Order().Should().Equal(expected);
        }

        // One read of the jobs, then per job one boundary, one page of ids and one delete: the count
        // of statements does not depend on how many runs are expired.
        issued.Count(c => c.Contains("OFFSET", StringComparison.Ordinal)).Should().Be(jobs.Length);
        issued.Count(c => c.StartsWith("DELETE", StringComparison.Ordinal)).Should().Be(jobs.Length);
        issued.Should().HaveCount(1 + (3 * jobs.Length));
    }

    [Fact]
    public async Task RunsSettledInTheSameInstantKeepTheHighestIdentitiesExactly()
    {
        // Three groups of equal instants; keeping four takes the whole newest group and the highest
        // identity of the next one.
        foreach (var ago in new[] { 30, 30, 20, 20, 20, 10 })
            await StoreAsync("a", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(ago));

        var before = await ReadAsync();
        var expected = before.OrderByDescending(e => e.SettledAt).ThenByDescending(e => e.Id)
            .Take(4).Select(e => e.Id).Order().ToList();

        (await PurgeAsync(keepLatest: 4)).Should().Be(2);

        (await ReadAsync()).Select(e => e.Id).Order().Should().Equal(expected);
        before.Where(e => !expected.Contains(e.Id)).Should().OnlyContain(e => e.SettledAt == (_now - TimeSpan.FromDays(30)).UtcDateTime);
    }

    [Fact]
    public async Task JobsThatCanNeverLoseARunDoNotCrowdOutAJobWithABacklog()
    {
        // Five jobs with a single, very old run each sort ahead of the busy one by their oldest run but
        // have nothing to delete under keep-latest 2. With a cap of three jobs per batch the busy job is
        // reached only if they are left out of the list.
        for (var index = 0; index < 5; index++)
            await StoreAsync("stuck" + index.ToString(CultureInfo.InvariantCulture), JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(90));

        await ManyAsync("busy", 10);

        using var scope = _provider.CreateScope();
        var pass = new JobRunRetentionPass(
            scope.ServiceProvider.GetRequiredService<IJobDbContext>(),
            Options.Create(new BsftSchedulingOptions { JobRunRetentionKeepLatest = 2 }),
            maxJobsPerBatch: 3);

        (await pass.PurgeBatchAsync(BatchSize, _cutoff)).Should().Be(8);

        var left = await ReadAsync();
        left.Count(e => e.JobName == "busy").Should().Be(2);
        left.Count(e => e.JobName.StartsWith("stuck", StringComparison.Ordinal)).Should().Be(5);
    }

    [Fact]
    public async Task TheBatchCapHoldsAcrossJobsAndSuccessiveBatchesDrainTheBacklog()
    {
        foreach (var job in new[] { "a", "b", "c" })
            await ManyAsync(job, 20);

        (await PurgeAsync(batchSize: 25, keepLatest: 2)).Should().Be(25);
        (await ReadAsync()).Should().HaveCount(60 - 25);

        while (await PurgeAsync(batchSize: 25, keepLatest: 2) > 0)
        {
        }

        (await ReadAsync()).Should().HaveCount(6);
    }

    [Fact]
    public async Task TheJobHoldingTheOldestExpiredRunIsPurgedFirst()
    {
        await StoreAsync("a-late", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(20));
        await StoreAsync("a-late", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(10));
        await StoreAsync("z-early", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(40));
        await StoreAsync("z-early", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(10));

        (await PurgeAsync(batchSize: 1, keepLatest: 1)).Should().Be(1);

        (await ReadAsync()).Should().NotContain(e => e.JobName == "z-early" && e.SettledAt < (_now - TimeSpan.FromDays(30)).UtcDateTime);
    }

    [Fact]
    public async Task AJobWithFewerSettledRunsThanTheOnesToKeepLosesNothing()
    {
        await StoreAsync("a", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(40));
        await StoreAsync("a", JobRunStatus.Failed, settledAgo: TimeSpan.FromDays(30));

        (await PurgeAsync(keepLatest: 5)).Should().Be(0);
        (await ReadAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task ABacklogLargerThanOneBatchIsPurgedNoFurtherThanTheCap()
    {
        await BacklogAsync(BatchSize + 1);

        (await PurgeAsync()).Should().Be(BatchSize);
        (await PurgeAsync()).Should().Be(1);
        (await ReadAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ABatchTakesTheRunsThatSettledLongestAgo()
    {
        await StoreAsync("oldest", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(30));
        await StoreAsync("middle", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(20));
        await StoreAsync("newest", JobRunStatus.Completed, settledAgo: TimeSpan.FromDays(10));

        (await PurgeAsync(batchSize: 2)).Should().Be(2);
        (await ReadAsync()).Should().ContainSingle().Which.JobName.Should().Be("newest");
    }

    [Fact]
    public async Task AnEmptyTableIsPurgedWithoutADelete()
    {
        (await PurgeAsync()).Should().Be(0);
    }

    private async Task<int> PurgeAsync(int batchSize = BatchSize, int keepLatest = 0)
    {
        using var scope = _provider.CreateScope();
        var options = Options.Create(new BsftSchedulingOptions { JobRunRetentionKeepLatest = keepLatest });
        var pass = new JobRunRetentionPass(scope.ServiceProvider.GetRequiredService<IJobDbContext>(), options);

        return await pass.PurgeBatchAsync(batchSize, _cutoff);
    }

    private async Task StoreAsync(string job, JobRunStatus status, TimeSpan? settledAgo, TimeSpan? dueAgo = null)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IJobDbContext>();
        db.JobRuns.Add(Row(job, status, settledAgo, dueAgo ?? settledAgo ?? TimeSpan.Zero));
        await db.SaveChangesAsync();
    }

    private async Task ManyAsync(string job, int count)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IJobDbContext>();

        for (var index = 0; index < count; index++)
        {
            var ago = TimeSpan.FromDays(30) + TimeSpan.FromMinutes(index);
            db.JobRuns.Add(Row(job, JobRunStatus.Completed, ago, ago));
        }

        await db.SaveChangesAsync();
    }

    private async Task BacklogAsync(int count)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IJobDbContext>();

        for (var index = 0; index < count; index++)
        {
            var ago = TimeSpan.FromDays(30) + TimeSpan.FromSeconds(index);
            db.JobRuns.Add(Row("job" + index.ToString(CultureInfo.InvariantCulture), JobRunStatus.Completed, ago, ago));
        }

        await db.SaveChangesAsync();
    }

    private static JobRunEntity Row(string job, JobRunStatus status, TimeSpan? settledAgo, TimeSpan dueAgo) => new()
    {
        JobName = job,
        Status = status,
        DueAt = (_now - dueAgo).UtcDateTime,
        SettledAt = settledAgo is { } ago ? (_now - ago).UtcDateTime : null,
        ConcurrencyToken = Guid.NewGuid(),
    };

    private async Task<List<JobRunEntity>> ReadAsync()
    {
        using var scope = _provider.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IJobDbContext>().JobRuns.AsNoTracking().ToListAsync();
    }
}
