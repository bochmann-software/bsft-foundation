using Bsft.Scheduling.EFCore.Tests.Fakes;
using Bsft.Scheduling.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests;

/// <summary>
/// What the retention pass does when another sweep removes a job's runs after the pass listed the job
/// and before it read the job's boundary.
/// </summary>
public sealed class JobRunRetentionPassConcurrentSweepTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public JobRunRetentionPassConcurrentSweepTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task PurgeBatchAsync_WhenTheRunsVanishBetweenListingAndBoundary_DeletesNothing()
    {
        // The boundary read is the second query of the context; the listing is the first.
        var sweep = new SweepingCommandInterceptor(2, "DELETE FROM JobRuns WHERE JobName = 'a'");
        await using (var seed = CreateContext())
        {
            await seed.Database.EnsureCreatedAsync();
            for (var index = 0; index < 3; index++)
                seed.JobRuns.Add(Row("a", TimeSpan.FromDays(30 + index)));
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(sweep);

        var options = Options.Create(new BsftSchedulingOptions { JobRunRetentionKeepLatest = 1 });
        var purged = await new JobRunRetentionPass(db, options).PurgeBatchAsync(100, _now - TimeSpan.FromDays(7));

        purged.Should().Be(0);
        (await db.JobRuns.AsNoTracking().CountAsync()).Should().Be(0);
    }

    private TestJobDbContext CreateContext(SweepingCommandInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<TestJobDbContext>().UseSqlite(_connection);
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);

        return new TestJobDbContext(builder.Options);
    }

    private static JobRunEntity Row(string job, TimeSpan settledAgo) => new()
    {
        JobName = job,
        Status = JobRunStatus.Completed,
        DueAt = (_now - settledAgo).UtcDateTime,
        SettledAt = (_now - settledAgo).UtcDateTime,
        ConcurrencyToken = Guid.NewGuid(),
    };
}
