using System.Globalization;
using Bsft.Scheduling.EFCore.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests;

/// <summary>
/// The retention pass against the provider it runs on in production: the grouped listing with its
/// HAVING, the per-job boundary with its OFFSET, and the delete by identity list, none of which a
/// SQLite run proves translates on PostgreSQL. Every fact skips without a server, because there is no
/// runtime skip in xunit v2 and a machine without one must not fail the suite.
/// </summary>
public class JobRunRetentionPassPostgresTests : IClassFixture<PostgresDatabaseFixture>
{
    private const int BatchSize = 500;

    private static readonly DateTimeOffset _now = new(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _cutoff = _now - TimeSpan.FromDays(7);

    private readonly PostgresDatabaseFixture _postgres;

    public JobRunRetentionPassPostgresTests(PostgresDatabaseFixture postgres) => _postgres = postgres;

    [PostgresFact]
    public async Task OnlyTheRunsThatSettledBeforeTheCutoffArePurged()
    {
        var options = await _postgres.CreateDatabaseAsync("cutoff");
        await StoreAsync(options, "job", JobRunStatus.Completed, TimeSpan.FromDays(30));
        await StoreAsync(options, "job", JobRunStatus.Failed, TimeSpan.FromDays(30));
        await StoreAsync(options, "job", JobRunStatus.Skipped, TimeSpan.FromDays(30));
        await StoreAsync(options, "job", JobRunStatus.Completed, TimeSpan.FromHours(1));

        (await PurgeAsync(options)).Should().Be(3);

        (await ReadAsync(options)).Should().ContainSingle().Which.SettledAt
            .Should().Be((_now - TimeSpan.FromHours(1)).UtcDateTime);
    }

    [PostgresFact]
    public async Task PendingAndRunningRunsAreNeverDeletedWithOrWithoutKeepLatest()
    {
        var options = await _postgres.CreateDatabaseAsync("active");
        await StoreAsync(options, "a", JobRunStatus.Pending, null, TimeSpan.FromDays(90));
        await StoreAsync(options, "b", JobRunStatus.Running, null, TimeSpan.FromDays(90));
        await StoreAsync(options, "b", JobRunStatus.Completed, TimeSpan.FromDays(60));

        (await PurgeAsync(options)).Should().Be(1);
        (await PurgeAsync(options, keepLatest: 1)).Should().Be(0);

        (await ReadAsync(options)).Select(e => e.Status)
            .Should().BeEquivalentTo([JobRunStatus.Pending, JobRunStatus.Running]);
    }

    [PostgresFact]
    public async Task KeepLatestLeavesExactlyTheNewestRunsOfEachJobIncludingTies()
    {
        var options = await _postgres.CreateDatabaseAsync("keep");

        // Job a: three groups of equal instants, keep four = the newest group and the highest identity
        // of the middle one. Job b: plain ordering.
        foreach (var ago in new[] { 30, 30, 20, 20, 20, 10 })
            await StoreAsync(options, "a", JobRunStatus.Completed, TimeSpan.FromDays(ago));

        for (var index = 0; index < 8; index++)
            await StoreAsync(options, "b", JobRunStatus.Completed, TimeSpan.FromDays(30) + TimeSpan.FromMinutes(index));

        var before = await ReadAsync(options);
        var expected = before.GroupBy(e => e.JobName)
            .SelectMany(g => g.OrderByDescending(e => e.SettledAt).ThenByDescending(e => e.Id).Take(4))
            .Select(e => e.Id).Order().ToList();

        (await PurgeAsync(options, keepLatest: 4)).Should().Be(before.Count - expected.Count);

        (await ReadAsync(options)).Select(e => e.Id).Order().Should().Equal(expected);
    }

    [PostgresFact]
    public async Task OnlyJobsWithSomethingDeletableAreListedSoStuckJobsDoNotCrowdOutABacklog()
    {
        var options = await _postgres.CreateDatabaseAsync("having");

        for (var index = 0; index < 5; index++)
            await StoreAsync(options, "stuck" + index.ToString(CultureInfo.InvariantCulture), JobRunStatus.Completed, TimeSpan.FromDays(90));

        for (var index = 0; index < 10; index++)
            await StoreAsync(options, "busy", JobRunStatus.Completed, TimeSpan.FromDays(30) + TimeSpan.FromMinutes(index));

        await using var db = new TestPostgresJobDbContext(options);
        var pass = new JobRunRetentionPass(
            db, Microsoft.Extensions.Options.Options.Create(new BsftSchedulingOptions { JobRunRetentionKeepLatest = 2 }), maxJobsPerBatch: 3);

        (await pass.PurgeBatchAsync(BatchSize, _cutoff)).Should().Be(8);

        var left = await ReadAsync(options);
        left.Count(e => e.JobName == "busy").Should().Be(2);
        left.Count(e => e.JobName.StartsWith("stuck", StringComparison.Ordinal)).Should().Be(5);
    }

    [PostgresFact]
    public async Task TheBatchCapHoldsAcrossJobsAndSuccessiveBatchesDrainTheBacklog()
    {
        var options = await _postgres.CreateDatabaseAsync("cap");

        foreach (var job in new[] { "a", "b", "c" })
        {
            for (var index = 0; index < 20; index++)
                await StoreAsync(options, job, JobRunStatus.Completed, TimeSpan.FromDays(30) + TimeSpan.FromMinutes(index));
        }

        (await PurgeAsync(options, batchSize: 25, keepLatest: 2)).Should().Be(25);
        (await ReadAsync(options)).Should().HaveCount(60 - 25);

        while (await PurgeAsync(options, batchSize: 25, keepLatest: 2) > 0)
        {
        }

        (await ReadAsync(options)).Should().HaveCount(6);
    }

    private static async Task<int> PurgeAsync(
        DbContextOptions<TestPostgresJobDbContext> options, int batchSize = BatchSize, int keepLatest = 0)
    {
        await using var db = new TestPostgresJobDbContext(options);
        var pass = new JobRunRetentionPass(
            db, Microsoft.Extensions.Options.Options.Create(new BsftSchedulingOptions { JobRunRetentionKeepLatest = keepLatest }));

        return await pass.PurgeBatchAsync(batchSize, _cutoff);
    }

    private static async Task StoreAsync(
        DbContextOptions<TestPostgresJobDbContext> options, string job, JobRunStatus status, TimeSpan? settledAgo, TimeSpan? dueAgo = null)
    {
        await using var db = new TestPostgresJobDbContext(options);
        db.JobRuns.Add(new JobRunEntity
        {
            JobName = job,
            Status = status,
            DueAt = (_now - (dueAgo ?? settledAgo ?? TimeSpan.Zero)).UtcDateTime,
            SettledAt = settledAgo is { } ago ? (_now - ago).UtcDateTime : null,
            ConcurrencyToken = Guid.NewGuid(),
        });

        await db.SaveChangesAsync();
    }

    private static async Task<List<JobRunEntity>> ReadAsync(DbContextOptions<TestPostgresJobDbContext> options)
    {
        await using var db = new TestPostgresJobDbContext(options);

        return await db.JobRuns.AsNoTracking().ToListAsync();
    }
}
