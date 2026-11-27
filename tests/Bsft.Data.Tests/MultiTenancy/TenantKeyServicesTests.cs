using Bsft.Core.Persistence;
using Bsft.Core.Tenancy;
using Bsft.Data.DependencyInjection;
using Bsft.Data.MultiTenancy;
using Bsft.Data.Tests.Support;
using Bsft.Data.Tests.Support.Fakes;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

/// <summary>
/// Tests for tenant key services, including value generation and tenant resolution.
/// </summary>
public sealed class TenantKeyServicesTests
{
    [Fact]
    public async Task Add_WithTenantKeysEnabled_GeneratesIdAndFillsTenantIdFromScope()
    {
        var now = TimeOrderedIdGenerator.Epoch.AddDays(3);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new ManualTimeProvider(now));
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext("tenant-1"));
        services.AddDbContext<TenantKeyTestDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()),
            contextLifetime: ServiceLifetime.Transient);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TenantKeyTestDbContext>();
        var entity = new TenantKeyedTestEntity { Name = "test", Age = 30 };

        context.TenantKeyedEntities.Add(entity);
        await context.SaveChangesAsync();

        entity.Id.Should().NotBe(0);
        TimeOrderedIdGenerator.Decompose(entity.Id).CreatedAt.Should().Be(now);
        entity.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public async Task Add_WhenTheHostRegisteredTheTenantKeys_GeneratesTheIdWithItsGenerator()
    {
        var services = new ServiceCollection();
        services.AddBsftDataTenantKeys(o => o.NodeId = 3);
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext("tenant-1"));
        services.AddDbContext<TenantKeyTestDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()),
            contextLifetime: ServiceLifetime.Transient);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TenantKeyTestDbContext>();
        var entity = new TenantKeyedTestEntity { Name = "test", Age = 30 };

        context.TenantKeyedEntities.Add(entity);
        await context.SaveChangesAsync();

        TimeOrderedIdGenerator.Decompose(entity.Id).NodeId.Should().Be(3);
    }

    [Fact]
    public void Add_WhenTheServicesHaveNoClock_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext("tenant-1"));
        using var provider = services.BuildServiceProvider();

        var options = new DbContextOptionsBuilder<TenantKeyTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseApplicationServiceProvider(provider)
            .Options;

        using var context = new TenantKeyTestDbContext(options);
        var entity = new TenantKeyedTestEntity { Name = "test", Age = 30 };

        var act = () => context.TenantKeyedEntities.Add(entity);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TenantKeyTestDbContext has no TimeProvider*AddBsftDataTenantKeys*");
    }

    [Fact]
    public void Add_WhenTheContextHasNoApplicationServices_ThrowsInvalidOperationException()
    {
        var options = new DbContextOptionsBuilder<TenantKeyTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TenantKeyTestDbContext(options);
        var entity = new TenantKeyedTestEntity { TenantId = "tenant-1", Name = "test", Age = 30 };

        var act = () => context.TenantKeyedEntities.Add(entity);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*has no TimeProvider*");
    }

    [Fact]
    public async Task Add_WithExplicitTenantId_KeepsProvidedValue()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new ManualTimeProvider(TimeOrderedIdGenerator.Epoch.AddDays(1)));
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext("auto-tenant"));
        services.AddDbContext<TenantKeyTestDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()),
            contextLifetime: ServiceLifetime.Transient);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TenantKeyTestDbContext>();
        var entity = new TenantKeyedTestEntity { TenantId = "explicit-tenant", Name = "test", Age = 30 };

        context.TenantKeyedEntities.Add(entity);
        await context.SaveChangesAsync();

        entity.TenantId.Should().Be("explicit-tenant");
    }

    [Fact]
    public void Add_WithoutAnyTenant_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new ManualTimeProvider(TimeOrderedIdGenerator.Epoch.AddDays(1)));
        using var provider = services.BuildServiceProvider();

        var options = new DbContextOptionsBuilder<TenantKeyTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseApplicationServiceProvider(provider)
            .Options;

        using var context = new TenantKeyTestDbContext(options);
        var entity = new TenantKeyedTestEntity { Name = "test", Age = 30 };

        var act = () => context.TenantKeyedEntities.Add(entity);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot use the tenant-keyed table*");
    }

    [Fact]
    public void HasTenantKey_WithConvertedIntExpression_ThrowsArgumentException()
    {
        var builder = new ModelBuilder(new ConventionSet());
        var entityBuilder = builder.Entity<TenantKeyedTestEntity>();

        var act = () => entityBuilder.HasTenantKey(
            e => e.TenantId,
            e => (long)e.Age  // Using Age (int) with a cast instead of a property
        );

        act.Should().Throw<ArgumentException>()
            .WithMessage("*The expression must name a property*");
    }

    [Fact]
    public void FindCurrentTenantId_WithTenantContext_ReturnsTenantId()
    {
        var tenantContext = new FakeTenantContext("my-tenant");

        var result = tenantContext.FindCurrentTenantId();

        result.Should().Be("my-tenant");
    }

    [Fact]
    public void FindCurrentTenantId_WithNullTenantContext_ReturnsBackgroundTenant()
    {
        ITenantContext? tenantContext = null;
        var result = tenantContext.FindCurrentTenantId();

        // When no tenant context exists, it returns BackgroundTenant.Current
        result.Should().Be(BackgroundTenant.Current);
    }

    [Fact]
    public void FindCurrentTenantId_WithEmptyTenantId_ReturnsBackgroundTenant()
    {
        var tenantContext = new FakeTenantContext("");

        var result = tenantContext.FindCurrentTenantId();

        // When tenant context has empty tenant id, it falls back to BackgroundTenant.Current
        result.Should().Be(BackgroundTenant.Current);
    }

    [Fact]
    public void FindCurrentTenantId_WithServiceProvider_GetsTenantFromContext()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext("service-tenant"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var result = scope.ServiceProvider.FindCurrentTenantId();

        result.Should().Be("service-tenant");
    }

    [Fact]
    public void GetCurrentTenantId_WithTenant_ReturnsIt()
    {
        var tenantContext = new FakeTenantContext("my-tenant");

        var result = tenantContext.GetCurrentTenantId("MyTable");

        result.Should().Be("my-tenant");
    }

    [Fact]
    public void GetCurrentTenantId_WithoutTenant_ThrowsWithTableName()
    {
        ITenantContext? tenantContext = null;
        var act = () => tenantContext.GetCurrentTenantId("MyTable");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MyTable*");
    }
}
