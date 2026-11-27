using Bsft.Core.Persistence;
using Bsft.Data.MultiTenancy;
using Bsft.Data.Postgres.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Bsft.Data.Postgres.Tests;

/// <summary>
/// Tests for tenant-keyed table schema generation and multi-tenant operations on PostgreSQL.
/// </summary>
public sealed class PostgresTenantKeyTests : IAsyncLifetime
{
    private readonly PostgresDatabaseFixture _fixture = new();
    private string _connectionString = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _connectionString = await _fixture.CreateDatabaseAsync("tenant_key");
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    /// <summary>
    /// Adds the context's tables to the database the fixture created for this test. The database is
    /// neither dropped nor recreated: it is this test's own, and dropping it would race the fixture.
    /// </summary>
    private static Task CreateTablesAsync(DbContext context) =>
        context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();

    [PostgresFact]
    public async Task GenerateCreateScript_TenantKeyedTable_CreatesCompositeKey()
    {
        var options = new DbContextOptionsBuilder<TenantKeyTestContext>()
            .UseNpgsql(_connectionString)
            .Options;

        using var context = new TenantKeyTestContext(options);
        await CreateTablesAsync(context);

        var model = context.Model;
        var entityType = model.FindEntityType(typeof(TenantKeyedTestEntity));

        entityType.Should().NotBeNull();
        if (entityType == null)
        {
            return; // Skip if entity type not found
        }

        entityType.IsTenantKeyed().Should().BeTrue();

        var key = entityType.FindPrimaryKey();
        key.Should().NotBeNull();
        if (key != null)
        {
            key.Properties.Select(p => p.Name).Should().Equal(TenantKeys.TenantIdColumnName, "Id");
        }
    }

    [PostgresFact]
    public async Task InsertAndRead_TwoTenantsWithSameId_RoundTripSucceeds()
    {
        var options = new DbContextOptionsBuilder<TenantKeyTestContext>()
            .UseNpgsql(_connectionString)
            .Options;

        using var context = new TenantKeyTestContext(options);
        await CreateTablesAsync(context);

        // Insert for tenant-1
        var tenant1Entity = new TenantKeyedTestEntity
        {
            Id = 1000,
            TenantId = "tenant-1",
            Name = "Entity T1",
            Age = 30,
            Email = "t1@example.com",
            Price = 99.99m
        };
        context.TenantKeyedEntities.Add(tenant1Entity);
        await context.SaveChangesAsync();

        // Insert for tenant-2 with same Id
        var tenant2Entity = new TenantKeyedTestEntity
        {
            Id = 1000,
            TenantId = "tenant-2",
            Name = "Entity T2",
            Age = 25,
            Email = "t2@example.com",
            Price = 79.99m
        };
        context.TenantKeyedEntities.Add(tenant2Entity);
        await context.SaveChangesAsync();

        // Read back both entities
        var readT1 = await context.TenantKeyedEntities
            .FirstOrDefaultAsync(e => e.TenantId == "tenant-1" && e.Id == 1000);
        var readT2 = await context.TenantKeyedEntities
            .FirstOrDefaultAsync(e => e.TenantId == "tenant-2" && e.Id == 1000);

        readT1.Should().NotBeNull();
        readT1!.Name.Should().Be("Entity T1");
        readT1.Age.Should().Be(30);

        readT2.Should().NotBeNull();
        readT2!.Name.Should().Be("Entity T2");
        readT2.Age.Should().Be(25);
    }

    [PostgresFact]
    public async Task InsertDuplicate_SameTenantAndId_FailsWithUniqueConstraint()
    {
        var options = new DbContextOptionsBuilder<TenantKeyTestContext>()
            .UseNpgsql(_connectionString)
            .Options;

        using var context = new TenantKeyTestContext(options);
        await CreateTablesAsync(context);

        var entity1 = new TenantKeyedTestEntity
        {
            Id = 2000,
            TenantId = "tenant-1",
            Name = "First",
            Age = 30,
            Email = "first@example.com",
            Price = 50m
        };
        context.TenantKeyedEntities.Add(entity1);
        await context.SaveChangesAsync();

        // Try to insert duplicate with a fresh context
        using var context2 = new TenantKeyTestContext(options);
        var entity2 = new TenantKeyedTestEntity
        {
            Id = 2000,
            TenantId = "tenant-1",
            Name = "Second",
            Age = 31,
            Email = "second@example.com",
            Price = 60m
        };
        context2.TenantKeyedEntities.Add(entity2);

        await Assert.ThrowsAsync<DbUpdateException>(() => context2.SaveChangesAsync());
    }
}
