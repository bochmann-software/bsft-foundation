using Bsft.Scheduling.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests;

public class StoredScheduleEntityConfigurationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public StoredScheduleEntityConfigurationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    [Fact]
    public void ConfigureRejectsANullBuilder()
    {
        var act = () => new StoredScheduleEntityConfiguration().Configure(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void TheModelCarriesTheKeyTheLengthsAndTheConcurrencyToken()
    {
        using var db = CreateContext();

        var entityType = db.Model.FindEntityType(typeof(StoredScheduleEntity))!;

        entityType.GetTableName().Should().Be("StoredSchedules");
        entityType.FindPrimaryKey()!.Properties.Select(p => p.Name).Should().Equal(nameof(StoredScheduleEntity.Id));
        entityType.FindProperty(nameof(StoredScheduleEntity.JobKey))!.IsNullable.Should().BeFalse();
        entityType.FindProperty(nameof(StoredScheduleEntity.JobKey))!.GetMaxLength().Should().Be(500);
        entityType.FindProperty(nameof(StoredScheduleEntity.TenantId))!.GetMaxLength().Should().Be(200);
        entityType.FindProperty(nameof(StoredScheduleEntity.CronExpression))!.GetMaxLength().Should().Be(200);
        entityType.FindProperty(nameof(StoredScheduleEntity.ConcurrencyToken))!.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void TheDuePollingIndexAndTheTenantIndexAreBothPresent()
    {
        using var db = CreateContext();

        var indexes = db.Model.FindEntityType(typeof(StoredScheduleEntity))!.GetIndexes().ToList();

        indexes.Should().Contain(i =>
            i.Properties.Count == 3
            && i.Properties[0].Name == nameof(StoredScheduleEntity.Enabled)
            && i.Properties[1].Name == nameof(StoredScheduleEntity.NextRunAt)
            && i.Properties[2].Name == nameof(StoredScheduleEntity.Id));

        indexes.Should().Contain(i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(StoredScheduleEntity.TenantId));
    }

    [Fact]
    public async Task TheTableIsCreatedAndAcceptsARow()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.StoredSchedules.Add(new StoredScheduleEntity
        {
            JobKey = "report",
            IntervalTicks = TimeSpan.FromMinutes(5).Ticks,
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid(),
        });

        var act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("0 7 * * *", 3000000000L)]
    public async Task TheTableRefusesARowThatIsNeitherACronNorAnIntervalSchedule(string? cron, long? ticks)
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.StoredSchedules.Add(new StoredScheduleEntity
        {
            JobKey = "report",
            CronExpression = cron,
            IntervalTicks = ticks,
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid(),
        });

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private TestStoredScheduleDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestStoredScheduleDbContext>().UseSqlite(_connection).Options);

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
