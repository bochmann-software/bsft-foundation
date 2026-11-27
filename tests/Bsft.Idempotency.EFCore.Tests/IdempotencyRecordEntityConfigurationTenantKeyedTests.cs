using FluentAssertions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Bsft.Idempotency.EFCore.Tests.Support;
using Bsft.Tests.Shared;

namespace Bsft.Idempotency.EFCore.Tests;

public sealed class IdempotencyRecordEntityConfigurationTenantKeyedTests : IDisposable
{
    private readonly SqliteFixture _sqliteUnkeyed = new();
    private readonly TestIdempotencyDbContext _contextUnkeyed;
    private readonly PostgresDatabaseFixture _postgresKeyed = new();
    private readonly TenantKeyedTestIdempotencyDbContext _contextKeyed;

    public IdempotencyRecordEntityConfigurationTenantKeyedTests()
    {
        _contextUnkeyed = _sqliteUnkeyed.NewContext();
        _contextKeyed = _postgresKeyed.NewContext();
    }

    public void Dispose()
    {
        _contextKeyed.Dispose();
        _postgresKeyed.Dispose();
        _contextUnkeyed.Dispose();
        _sqliteUnkeyed.Dispose();
    }

    private IEntityType EntityKeyed => _contextKeyed.Model.FindEntityType(typeof(IdempotencyRecordEntity))!;
    private IEntityType EntityUnkeyed => _contextUnkeyed.Model.FindEntityType(typeof(IdempotencyRecordEntity))!;

    [Fact]
    public void WithTenantKeys_TenantIdColumnIsRequired()
    {
        var tenantIdProperty = EntityKeyed.FindProperty(nameof(IdempotencyRecordEntity.TenantId))!;

        tenantIdProperty.IsNullable.Should().BeFalse();
        tenantIdProperty.GetMaxLength().Should().Be(128);
    }

    [Fact]
    public void WithoutTenantKeys_TenantIdColumnIsIgnored()
    {
        var tenantIdProperty = EntityUnkeyed.FindProperty(nameof(IdempotencyRecordEntity.TenantId));

        // Without tenant keys, the property should be ignored.
        tenantIdProperty.Should().BeNull();
    }

    [Fact]
    public void WithTenantKeys_KeyIsCompositeOfTenantIdAndId()
    {
        var keyProperties = EntityKeyed.FindPrimaryKey()!.Properties.Select(p => p.Name).ToList();

        keyProperties.Should().Equal(
            nameof(IdempotencyRecordEntity.TenantId),
            nameof(IdempotencyRecordEntity.Id));
    }

    [Fact]
    public void WithoutTenantKeys_KeyIsSurrogateIdOnly()
    {
        var keyProperties = EntityUnkeyed.FindPrimaryKey()!.Properties.Select(p => p.Name).ToList();

        keyProperties.Should().Equal(nameof(IdempotencyRecordEntity.Id));
    }

    [Fact]
    public void WithTenantKeys_UniqueIndexIncludesTenantId()
    {
        var uniqueIndex = EntityKeyed.GetIndexes()
            .Should().Contain(index =>
                index.IsUnique && index.Properties.Count == 3
                && index.Properties[0].Name == nameof(IdempotencyRecordEntity.TenantId)
                && index.Properties[1].Name == nameof(IdempotencyRecordEntity.Scope)
                && index.Properties[2].Name == nameof(IdempotencyRecordEntity.Key))
            .Subject;

        // Verify it actually exists.
        uniqueIndex.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void WithoutTenantKeys_UniqueIndexOmitsTenantId()
    {
        var uniqueIndex = EntityUnkeyed.GetIndexes()
            .Should().Contain(index =>
                index.IsUnique && index.Properties.Count == 2
                && index.Properties[0].Name == nameof(IdempotencyRecordEntity.Scope)
                && index.Properties[1].Name == nameof(IdempotencyRecordEntity.Key))
            .Subject;

        uniqueIndex.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void WithTenantKeys_ExpiryIndexIncludesTenantId()
    {
        var expiryIndex = EntityKeyed.GetIndexes()
            .Should().Contain(index =>
                index.Properties.Count == 2
                && index.Properties[0].Name == nameof(IdempotencyRecordEntity.TenantId)
                && index.Properties[1].Name == nameof(IdempotencyRecordEntity.ExpiresAt))
            .Subject;

        expiryIndex.IsUnique.Should().BeFalse();
    }

    [Fact]
    public void WithoutTenantKeys_ExpiryIndexOmitsTenantId()
    {
        var expiryIndex = EntityUnkeyed.GetIndexes()
            .Should().Contain(index =>
                index.Properties.Count == 1
                && index.Properties[0].Name == nameof(IdempotencyRecordEntity.ExpiresAt))
            .Subject;

        expiryIndex.IsUnique.Should().BeFalse();
    }

    [Fact]
    public void WithTenantKeys_TenantIdLeadsTheUniqueIndex()
    {
        // This ensures one tenant's keys cannot collide with another's, and all queries ride the index.
        var uniqueIndex = EntityKeyed.GetIndexes()
            .Single(index => index.IsUnique && index.Properties.Count == 3);

        uniqueIndex.Properties[0].Name.Should().Be(nameof(IdempotencyRecordEntity.TenantId));
    }

    [Fact]
    public void WithTenantKeys_TenantIdLeadsTheExpiryIndex()
    {
        // The sweep runs inside a BackgroundTenant, so every lookup is scoped by tenant.
        var expiryIndex = EntityKeyed.GetIndexes()
            .Single(index => index.Properties.Count == 2
                && index.Properties[1].Name == nameof(IdempotencyRecordEntity.ExpiresAt));

        expiryIndex.Properties[0].Name.Should().Be(nameof(IdempotencyRecordEntity.TenantId));
    }

    [PostgresFact]
    public async Task WithTenantKeys_DatabaseRefusesASecondRowUnderOneTenantScopeAndKey()
    {
        await using var first = _postgresKeyed.NewContext();
        first.IdempotencyRecords.Add(Record(tenantId: "tenant-a"));
        await first.SaveChangesAsync();

        await using var second = _postgresKeyed.NewContext();
        second.IdempotencyRecords.Add(Record(tenantId: "tenant-a"));

        var act = async () => await second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [PostgresFact]
    public async Task WithTenantKeys_DifferentTenantsCanShareTheSameScopeAndKey()
    {
        await using var first = _postgresKeyed.NewContext();
        first.IdempotencyRecords.Add(Record(tenantId: "tenant-a"));
        await first.SaveChangesAsync();

        await using var second = _postgresKeyed.NewContext();
        second.IdempotencyRecords.Add(Record(tenantId: "tenant-b"));

        // This should succeed because the unique index includes TenantId.
        var act = async () => await second.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WithoutTenantKeys_DifferentTenantsCannotShareTheSameScopeAndKey()
    {
        await using var first = _sqliteUnkeyed.NewContext();
        first.IdempotencyRecords.Add(Record(tenantId: "tenant-a"));
        await first.SaveChangesAsync();

        await using var second = _sqliteUnkeyed.NewContext();
        second.IdempotencyRecords.Add(Record(tenantId: "tenant-b"));

        // Without tenant keys in the index, the second insert should fail.
        var act = async () => await second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private static IdempotencyRecordEntity Record(string tenantId = "test-tenant") => new()
    {
        TenantId = tenantId,
        Scope = "scope",
        Key = "key",
        Fingerprint = "fingerprint",
        ExpiresAt = new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc),
    };
}
