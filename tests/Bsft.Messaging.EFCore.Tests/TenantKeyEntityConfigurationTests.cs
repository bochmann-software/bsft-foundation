using Bsft.Data.MultiTenancy;
using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Outbox;
using Bsft.Messaging.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Bsft.Tests.Shared;

namespace Bsft.Messaging.EFCore.Tests;

/// <summary>
/// Verifies the schema generation for <see cref="InboxMessageEntity"/> and <see cref="OutboxMessageEntity"/>
/// with and without tenant keys enabled.
/// </summary>
public sealed class TenantKeyEntityConfigurationTests
{
    [Fact]
    public void Outbox_message_entity_without_tenant_keys_has_identity_key_and_no_tenant_column()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new TestOutboxDbContext(options);
        db.Database.EnsureCreated();

        // Act & Assert
        var entityType = db.Model.FindEntityType(typeof(OutboxMessageEntity))!;
        var keys = entityType.GetKeys().ToArray();
        var tenantIdProperty = entityType.FindProperty(nameof(OutboxMessageEntity.TenantId));

        // Should have a single Id key
        keys.Should().ContainSingle();
        keys[0].Properties.Should().ContainSingle().Which.Name.Should().Be(nameof(OutboxMessageEntity.Id));

        // Should have no TenantId property mapped
        tenantIdProperty.Should().BeNull("without tenant keys, TenantId should be ignored");
    }

    [Fact]
    public void Inbox_message_entity_without_tenant_keys_has_identity_key_and_no_tenant_column()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new TestOutboxDbContext(options);
        db.Database.EnsureCreated();

        // Act & Assert
        var entityType = db.Model.FindEntityType(typeof(InboxMessageEntity))!;
        var keys = entityType.GetKeys().ToArray();
        var tenantIdProperty = entityType.FindProperty(nameof(InboxMessageEntity.TenantId));

        // Should have a single Id key
        keys.Should().ContainSingle();
        keys[0].Properties.Should().ContainSingle().Which.Name.Should().Be(nameof(InboxMessageEntity.Id));

        // Should have no TenantId property mapped
        tenantIdProperty.Should().BeNull("without tenant keys, TenantId should be ignored");
    }

    [Fact]
    public void Inbox_unique_index_without_tenant_keys_is_consumer_event_id()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new TestOutboxDbContext(options);
        db.Database.EnsureCreated();

        // Act & Assert
        var entityType = db.Model.FindEntityType(typeof(InboxMessageEntity))!;
        var indexes = entityType.GetIndexes();

        // Should have a unique index on (Consumer, EventId)
        var uniqueIndex = indexes.FirstOrDefault(i => i.IsUnique);
        uniqueIndex.Should().NotBeNull("should have a unique index");
        uniqueIndex!.Properties.Select(p => p.Name).Should().Equal(
            nameof(InboxMessageEntity.Consumer),
            nameof(InboxMessageEntity.EventId));
    }

    [Fact]
    public void Outbox_query_index_without_tenant_keys_includes_processing_timestamps()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new TestOutboxDbContext(options);
        db.Database.EnsureCreated();

        // Act & Assert
        var entityType = db.Model.FindEntityType(typeof(OutboxMessageEntity))!;
        var indexes = entityType.GetIndexes();

        // Should have an index for the polling query
        var pollingIndex = indexes.FirstOrDefault(i =>
            i.Properties.Any(p => p.Name == nameof(OutboxMessageEntity.ProcessedAt)));
        pollingIndex.Should().NotBeNull("should have an index for polling query");
        pollingIndex!.Properties.Select(p => p.Name).Should().Contain(
            nameof(OutboxMessageEntity.ProcessedAt),
            nameof(OutboxMessageEntity.DeadLetteredAt),
            nameof(OutboxMessageEntity.LeasedUntil),
            nameof(OutboxMessageEntity.NextAttemptAt),
            nameof(OutboxMessageEntity.Id));
    }

    [Fact]
    public void Outbox_message_entity_with_tenant_keys_has_composite_key_and_tenant_column()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestTenantKeyedDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new TestTenantKeyedDbContext(options);
        db.Database.EnsureCreated();

        // Act & Assert
        var entityType = db.Model.FindEntityType(typeof(OutboxMessageEntity))!;
        var keys = entityType.GetKeys().ToArray();
        var tenantIdProperty = entityType.FindProperty(nameof(OutboxMessageEntity.TenantId));

        // Should have a composite (TenantId, Id) key
        keys.Should().ContainSingle();
        keys[0].Properties.Select(p => p.Name).Should().Equal(
            nameof(OutboxMessageEntity.TenantId),
            nameof(OutboxMessageEntity.Id));

        // TenantId should be non-nullable and not identity
        tenantIdProperty.Should().NotBeNull();
        tenantIdProperty!.IsNullable.Should().BeFalse();
        tenantIdProperty.ValueGenerated.Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never);
    }

    [Fact]
    public void Inbox_message_entity_with_tenant_keys_has_composite_key_and_tenant_column()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestTenantKeyedDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new TestTenantKeyedDbContext(options);
        db.Database.EnsureCreated();

        // Act & Assert
        var entityType = db.Model.FindEntityType(typeof(InboxMessageEntity))!;
        var keys = entityType.GetKeys().ToArray();
        var tenantIdProperty = entityType.FindProperty(nameof(InboxMessageEntity.TenantId));

        // Should have a composite (TenantId, Id) key
        keys.Should().ContainSingle();
        keys[0].Properties.Select(p => p.Name).Should().Equal(
            nameof(InboxMessageEntity.TenantId),
            nameof(InboxMessageEntity.Id));

        // TenantId should be non-nullable
        tenantIdProperty.Should().NotBeNull();
        tenantIdProperty!.IsNullable.Should().BeFalse();
        tenantIdProperty.ValueGenerated.Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never);
    }

    [Fact]
    public void Inbox_unique_index_with_tenant_keys_includes_tenant_id()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestTenantKeyedDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new TestTenantKeyedDbContext(options);
        db.Database.EnsureCreated();

        // Act & Assert
        var entityType = db.Model.FindEntityType(typeof(InboxMessageEntity))!;
        var indexes = entityType.GetIndexes();

        // Should have a unique index on (TenantId, Consumer, EventId)
        var uniqueIndex = indexes.FirstOrDefault(i => i.IsUnique);
        uniqueIndex.Should().NotBeNull("should have a unique index");
        uniqueIndex!.Properties.Select(p => p.Name).Should().Equal(
            nameof(InboxMessageEntity.TenantId),
            nameof(InboxMessageEntity.Consumer),
            nameof(InboxMessageEntity.EventId));
    }

    [Fact]
    public void Outbox_query_index_with_tenant_keys_leads_with_tenant_id()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestTenantKeyedDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new TestTenantKeyedDbContext(options);
        db.Database.EnsureCreated();

        // Act & Assert
        var entityType = db.Model.FindEntityType(typeof(OutboxMessageEntity))!;
        var indexes = entityType.GetIndexes();

        // Should have an index for the polling query starting with TenantId
        var pollingIndex = indexes.FirstOrDefault(i =>
            i.Properties.Any(p => p.Name == nameof(OutboxMessageEntity.ProcessedAt)));
        pollingIndex.Should().NotBeNull("should have an index for polling query");

        var properties = pollingIndex!.Properties.Select(p => p.Name).ToList();
        properties.First().Should().Be(nameof(OutboxMessageEntity.TenantId));
        properties.Should().Contain(
            nameof(OutboxMessageEntity.ProcessedAt),
            nameof(OutboxMessageEntity.DeadLetteredAt),
            nameof(OutboxMessageEntity.LeasedUntil),
            nameof(OutboxMessageEntity.NextAttemptAt),
            nameof(OutboxMessageEntity.Id));
    }
}
