using Bsft.Core;
using Bsft.Data.DependencyInjection;
using Bsft.Data.MultiTenancy;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

/// <summary>
/// Tests for the TenantKeyedContexts utility class, verifying the registration discovery and lifetime filtering.
/// </summary>
public sealed class TenantKeyedContextsTests
{
    [Fact]
    public void Registered_NoContextsRegistered_ReturnsEmpty()
    {
        var services = new ServiceCollection();

        var result = TenantKeyedContexts.Registered(services);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Registered_SingleContextRegistered_ReturnsSingleContext()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        var result = TenantKeyedContexts.Registered(services).ToList();

        result.Should().HaveCount(1)
            .And.Contain(typeof(TestDbContextWithTenantKeys));
    }

    [Fact]
    public void Registered_MultipleContextsRegistered_ReturnsAllContexts()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddDbContext<TenantKeyTestDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        var result = TenantKeyedContexts.Registered(services).ToList();

        result.Should().HaveCount(2)
            .And.Contain(typeof(TestDbContextWithTenantKeys))
            .And.Contain(typeof(TenantKeyTestDbContext));
    }

    [Fact]
    public void Registered_DuplicateContextRegistered_ReturnsDistinct()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        var result = TenantKeyedContexts.Registered(services).ToList();

        result.Should().HaveCount(1)
            .And.Contain(typeof(TestDbContextWithTenantKeys));
    }

    [Fact]
    public void Registered_WithLifetimeFilter_ReturnsSingletonOnly()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()),
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContext<TenantKeyTestDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()),
            optionsLifetime: ServiceLifetime.Scoped);

        var result = TenantKeyedContexts.Registered(services, ServiceLifetime.Singleton).ToList();

        result.Should().HaveCount(1)
            .And.Contain(typeof(TestDbContextWithTenantKeys));
    }

    [Fact]
    public void Registered_WithLifetimeFilterScoped_ReturnsScopedOnly()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()),
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContext<TenantKeyTestDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()),
            optionsLifetime: ServiceLifetime.Scoped);

        var result = TenantKeyedContexts.Registered(services, ServiceLifetime.Scoped).ToList();

        result.Should().HaveCount(1)
            .And.Contain(typeof(TenantKeyTestDbContext));
    }

    [Fact]
    public void Registered_WithTransientLifetime_ReturnsEmpty()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()),
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContext<TenantKeyTestDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()),
            optionsLifetime: ServiceLifetime.Scoped);

        var result = TenantKeyedContexts.Registered(services, ServiceLifetime.Transient).ToList();

        result.Should().BeEmpty();
    }
}
