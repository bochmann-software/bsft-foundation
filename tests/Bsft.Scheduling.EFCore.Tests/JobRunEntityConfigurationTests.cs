using Bsft.Scheduling.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests;

/// <summary>
/// The mapping is exercised against a real database, because the invariant it carries — at most one
/// pending occurrence per recurring job — only exists as a filtered unique index in the schema.
/// </summary>
public class JobRunEntityConfigurationTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public JobRunEntityConfigurationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    [Fact]
    public void ConfigureRejectsANullBuilder()
    {
        var act = () => new JobRunEntityConfiguration().Configure(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void TheDefaultFilterIsTheOneSqlServerAndSqliteShare()
    {
        JobRunEntityConfiguration.DefaultPendingFilter.Should().Be("[IsRecurring] = 1 AND [Status] = 0");
    }

    [Fact]
    public void AssemblyScanningAppliesTheConfiguration()
    {
        // ApplyConfigurationsFromAssembly matches a parameterless constructor exactly, and an
        // all-optional one does not match it. A configuration that only offers the latter is skipped
        // in silence: the table still appears through the DbSet convention, but the filtered unique
        // index is gone and two instances seeding at the same moment double-execute the job forever.
        using var db = new ScannedJobDbContext(
            new DbContextOptionsBuilder<ScannedJobDbContext>().UseSqlite(_connection).Options);

        var occurrenceIndex = db.Model
            .FindEntityType(typeof(JobRunEntity))!
            .GetIndexes()
            .Single(index => index.Properties.Select(p => p.Name).SequenceEqual([nameof(JobRunEntity.JobName)]));

        occurrenceIndex.IsUnique.Should().BeTrue();
        occurrenceIndex.GetFilter().Should().Be(JobRunEntityConfiguration.DefaultPendingFilter);
    }

    [Fact]
    public async Task TheDatabaseRejectsASecondPendingOccurrenceOfTheSameRecurringJob()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.JobRuns.Add(Entity("cleanup", JobRunStatus.Pending, recurring: true));
        await db.SaveChangesAsync();

        db.JobRuns.Add(Entity("cleanup", JobRunStatus.Pending, recurring: true));

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ARunningOccurrenceMayCoexistWithAPendingSuccessor()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.JobRuns.Add(Entity("cleanup", JobRunStatus.Running, recurring: true));
        db.JobRuns.Add(Entity("cleanup", JobRunStatus.Pending, recurring: true));

        // Running rows sit outside the filter on purpose: under JobOverlapPolicy.Allow the successor
        // is written while the current occurrence is still going.
        var act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ASettledOccurrenceLeavesTheSlotFreeAgain()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.JobRuns.Add(Entity("cleanup", JobRunStatus.Completed, recurring: true));
        db.JobRuns.Add(Entity("cleanup", JobRunStatus.Failed, recurring: true));
        db.JobRuns.Add(Entity("cleanup", JobRunStatus.Skipped, recurring: true));
        db.JobRuns.Add(Entity("cleanup", JobRunStatus.Pending, recurring: true));

        // The index is filtered, so a settled row keeps its name without blocking the job forever.
        var act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ANonRecurringRunMayRepeatItsNameFreely()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.JobRuns.Add(Entity("report", JobRunStatus.Pending, recurring: false));
        db.JobRuns.Add(Entity("report", JobRunStatus.Pending, recurring: false));

        var act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ANullPendingFilterDropsTheUniquenessAltogether()
    {
        await using var db = CreateUnfilteredContext();
        await db.Database.EnsureCreatedAsync();

        db.JobRuns.Add(Entity("cleanup", JobRunStatus.Pending, recurring: true));
        db.JobRuns.Add(Entity("cleanup", JobRunStatus.Pending, recurring: true));

        var act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync("a provider that cannot express the filter loses the guarantee");
    }

    [Fact]
    public void TheModelCarriesTheKeyTheLengthsAndTheConcurrencyToken()
    {
        using var db = CreateContext();

        var entityType = db.Model.FindEntityType(typeof(JobRunEntity))!;

        entityType.Should().NotBeNull();
        entityType.GetTableName().Should().Be("JobRuns");
        entityType.FindPrimaryKey()!.Properties.Select(p => p.Name).Should().Equal(nameof(JobRunEntity.Id));
        entityType.FindProperty(nameof(JobRunEntity.JobName))!.IsNullable.Should().BeFalse();
        entityType.FindProperty(nameof(JobRunEntity.JobName))!.GetMaxLength().Should().Be(500);
        entityType.FindProperty(nameof(JobRunEntity.LeaseOwner))!.GetMaxLength().Should().Be(200);
        entityType.FindProperty(nameof(JobRunEntity.CorrelationId))!.GetMaxLength().Should().Be(200);
        entityType.FindProperty(nameof(JobRunEntity.ConcurrencyToken))!.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void ThePollingIndexAndTheOccurrenceIndexAreBothPresent()
    {
        using var db = CreateContext();

        var indexes = db.Model.FindEntityType(typeof(JobRunEntity))!.GetIndexes().ToList();

        // The id closes the polling key: ListClaimableAsync orders by DueAt and then by Id, so without
        // the tie-breaker in the index a backlog is sorted in full instead of paged off the index.
        indexes.Should().Contain(i =>
            i.Properties.Count == 3
            && i.Properties[0].Name == nameof(JobRunEntity.Status)
            && i.Properties[1].Name == nameof(JobRunEntity.DueAt)
            && i.Properties[2].Name == nameof(JobRunEntity.Id));

        // The lease-reclaim index deliberately stays without it: the poll does not order by
        // LeaseExpiresAt, so an appended id would widen the index without saving a sort.
        indexes.Should().Contain(i =>
            i.Properties.Count == 2
            && i.Properties[0].Name == nameof(JobRunEntity.Status)
            && i.Properties[1].Name == nameof(JobRunEntity.LeaseExpiresAt));

        var occurrence = indexes.Single(i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(JobRunEntity.JobName));
        occurrence.IsUnique.Should().BeTrue();
        occurrence.GetFilter().Should().Be(JobRunEntityConfiguration.DefaultPendingFilter);
    }

    [Fact]
    public void ANullPendingFilterLeavesTheJobNameIndexNonUnique()
    {
        using var db = CreateUnfilteredContext();

        var occurrence = db.Model.FindEntityType(typeof(JobRunEntity))!
            .GetIndexes()
            .Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(JobRunEntity.JobName));

        occurrence.IsUnique.Should().BeFalse();
        occurrence.GetFilter().Should().BeNull();
    }

    private TestJobDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestJobDbContext>().UseSqlite(_connection).Options);

    private UnfilteredJobDbContext CreateUnfilteredContext() =>
        new(new DbContextOptionsBuilder<UnfilteredJobDbContext>().UseSqlite(_connection).Options);

    private static JobRunEntity Entity(string jobName, JobRunStatus status, bool recurring) => new()
    {
        JobName = jobName,
        DueAt = _now.UtcDateTime,
        Status = status,
        IsRecurring = recurring,
        ConcurrencyToken = Guid.NewGuid(),
    };

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
