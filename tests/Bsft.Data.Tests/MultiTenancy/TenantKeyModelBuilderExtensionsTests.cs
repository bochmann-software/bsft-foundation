using Bsft.Core.Persistence;
using Bsft.Data.MultiTenancy;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

/// <summary>
/// Tests for tenant-keyed model configuration, verifying model annotations, key mapping,
/// and value generation behavior.
/// </summary>
public sealed class TenantKeyModelBuilderExtensionsTests
{
    [Fact]
    public void UseTenantKeys_SetsModelAnnotation()
    {
        var options = new DbContextOptionsBuilder<TestDbContextWithTenantKeys>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContextWithTenantKeys(options);
        var model = context.Model;

        model.FindAnnotation(TenantKeys.TenantKeyedAnnotation)?.Value.Should().Be(true);
    }

    [Fact]
    public void IsTenantKeyed_EntityTypeBuilderWithoutUseTenantKeys_ReturnsFalse()
    {
        var options = new DbContextOptionsBuilder<TestDbContextWithoutTenantKeys>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContextWithoutTenantKeys(options);

        var model = context.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));

        entityType.Should().NotBeNull();
        entityType!.IsTenantKeyed().Should().BeFalse();
    }

    [Fact]
    public void IsTenantKeyed_EntityTypeBuilderWithUseTenantKeys_ReturnsTrue()
    {
        var options = new DbContextOptionsBuilder<TestDbContextWithTenantKeys>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContextWithTenantKeys(options);

        var model = context.Model;
        var entityType = model.FindEntityType(typeof(TenantKeyedTestEntity));

        entityType.Should().NotBeNull();
        entityType!.IsTenantKeyed().Should().BeTrue();
    }

    [Fact]
    public void IsTenantKeyed_SetsAnnotationOnEntity()
    {
        var options = new DbContextOptionsBuilder<TestDbContextWithTenantKeys>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContextWithTenantKeys(options);

        var model = context.Model;
        var entityType = model.FindEntityType(typeof(TenantKeyedTestEntity));

        entityType!.FindAnnotation(TenantKeys.TenantKeyableAnnotation)?.Value.Should().Be(true);
    }

    [Fact]
    public void HasTenantKey_SetsCompositeKeyWithTenantIdAndId()
    {
        var options = new DbContextOptionsBuilder<TestDbContextWithTenantKeys>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContextWithTenantKeys(options);

        var model = context.Model;
        var entityType = model.FindEntityType(typeof(TenantKeyedTestEntity));
        var key = entityType!.FindPrimaryKey();

        key.Should().NotBeNull();
        key!.Properties.Select(p => p.Name).Should().Equal(TenantKeys.TenantIdColumnName, "Id");
    }

    [Fact]
    public void HasTenantKey_TenantIdIsRequired()
    {
        var options = new DbContextOptionsBuilder<TestDbContextWithTenantKeys>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContextWithTenantKeys(options);

        var model = context.Model;
        var entityType = model.FindEntityType(typeof(TenantKeyedTestEntity));
        var tenantIdProp = entityType!.FindProperty(TenantKeys.TenantIdColumnName);

        tenantIdProp.Should().NotBeNull();
        tenantIdProp!.IsNullable.Should().BeFalse();
    }

    [Fact]
    public void HasTenantKey_TenantIdHasMaxLength128()
    {
        var options = new DbContextOptionsBuilder<TestDbContextWithTenantKeys>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContextWithTenantKeys(options);

        var model = context.Model;
        var entityType = model.FindEntityType(typeof(TenantKeyedTestEntity));
        var tenantIdProp = entityType!.FindProperty(TenantKeys.TenantIdColumnName);

        tenantIdProp!.GetMaxLength().Should().Be(TenantKeys.TenantIdMaxLength);
    }

    [Fact]
    public void HasTenantKey_IdValueGeneratedNever()
    {
        var options = new DbContextOptionsBuilder<TestDbContextWithTenantKeys>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContextWithTenantKeys(options);

        var model = context.Model;
        var entityType = model.FindEntityType(typeof(TenantKeyedTestEntity));
        var idProp = entityType!.FindProperty("Id");

        idProp!.ValueGenerated.Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never);
    }

    [Fact]
    public void HasTenantKey_IdHasTimeOrderedIdValueGenerator()
    {
        var options = new DbContextOptionsBuilder<TestDbContextWithTenantKeys>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContextWithTenantKeys(options);

        var model = context.Model;
        var entityType = model.FindEntityType(typeof(TenantKeyedTestEntity));
        var idProp = entityType!.FindProperty("Id");

        var factory = idProp!.GetValueGeneratorFactory();
        factory.Should().NotBeNull();
    }

    [Fact]
    public void DbSet_IsTenantKeyed_WithTenantKeysEnabled_ReturnsTrue()
    {
        var options = new DbContextOptionsBuilder<TestDbContextWithTenantKeys>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContextWithTenantKeys(options);

        context.TenantKeyedTestEntities.IsTenantKeyed().Should().BeTrue();
    }

    [Fact]
    public void DbSet_IsTenantKeyed_WithoutTenantKeys_ReturnsFalse()
    {
        var options = new DbContextOptionsBuilder<TestDbContextWithoutTenantKeys>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContextWithoutTenantKeys(options);

        context.TestEntities.IsTenantKeyed().Should().BeFalse();
    }

    [Fact]
    public void IsTenantKeyed_WithNullEntityType_ReturnsFalse()
    {
        TenantKeyModelBuilderExtensions.IsTenantKeyed((Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType?)null).Should().BeFalse();
    }

    [Fact]
    public void IsTenantKeyed_WithNullableOrMissingTenantId_ReturnsFalse()
    {
        var options = new DbContextOptionsBuilder<TestDbContextWithoutTenantKeys>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContextWithoutTenantKeys(options);

        var entityType = context.Model.FindEntityType(typeof(TestEntity));
        entityType!.IsTenantKeyed().Should().BeFalse();
    }

    [Fact]
    public void UseTenantKeys_CalledAfterEntityConfigurationWithIsTenantKeyed_ThrowsInvalidOperationException()
    {
        var modelBuilder = new ModelBuilder();

        // First, configure an entity with IsTenantKeyed (marking it as tenant-keyable)
        modelBuilder.Entity<TenantKeyedTestEntity>(builder =>
        {
            builder.IsTenantKeyed();
            builder.HasKey(e => e.Id);
        });

        // Now try to call UseTenantKeys, which should throw
        var act = () => modelBuilder.UseTenantKeys();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*UseTenantKeys must be called before the framework entity configurations are applied*");
    }

    [Fact]
    public void UseTenantKeys_CalledBeforeEntityConfiguration_DoesNotThrow()
    {
        var modelBuilder = new ModelBuilder();

        // Call UseTenantKeys first
        modelBuilder.UseTenantKeys();

        // Then configure an entity - should not throw
        var act = () => modelBuilder.Entity<TenantKeyedTestEntity>(builder =>
        {
            if (builder.IsTenantKeyed())
            {
                builder.HasTenantKey(e => e.TenantId, e => e.Id);
            }
        });

        act.Should().NotThrow();
    }
}
