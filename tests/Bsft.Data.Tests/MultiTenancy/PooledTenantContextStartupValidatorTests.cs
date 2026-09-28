using Bsft.Core;
using Bsft.Data.DependencyInjection;
using Bsft.Data.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

public class PooledTenantContextStartupValidatorTests
{
    [Fact]
    public void Constructor_NullServices_ThrowsArgumentNullException()
    {
        var act = () => new PooledTenantContextStartupValidator(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public async Task ValidateAsync_NoDbContextRegistered_Succeeds()
    {
        var services = new ServiceCollection();
        var validator = new PooledTenantContextStartupValidator(services);

        var result = await validator.ValidateAsync(CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Message.Should().Be("No tenant-scoped DbContext is pooled.");
    }

    [Fact]
    public async Task ValidateAsync_TenantScopedContextAddedWithAddDbContext_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddDbContext<PooledTenantDbContext>(options => options.UseInMemoryDatabase("not-pooled"));
        var validator = new PooledTenantContextStartupValidator(services);

        var result = await validator.ValidateAsync(CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_PlainContextPooled_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddDbContextPool<PlainDbContext>(options => options.UseInMemoryDatabase("plain-pooled"));
        var validator = new PooledTenantContextStartupValidator(services);

        var result = await validator.ValidateAsync(CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_TenantScopedContextPooled_Fails()
    {
        var services = new ServiceCollection();
        services.AddDbContextPool<PooledTenantDbContext>(options => options.UseInMemoryDatabase("pooled"));
        var validator = new PooledTenantContextStartupValidator(services);

        var result = await validator.ValidateAsync(CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain(nameof(PooledTenantDbContext))
            .And.Contain("AddDbContextPool")
            .And.Contain("AddDbContext instead");
    }

    [Fact]
    public async Task ValidateAsync_RegistrationOrderDoesNotMatter()
    {
        var services = new ServiceCollection();
        var validator = new PooledTenantContextStartupValidator(services);

        services.AddDbContextPool<PooledTenantDbContext>(options => options.UseInMemoryDatabase("late"));

        var result = await validator.ValidateAsync(CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void AddBsftDataMultiTenancy_RegistersTheValidator()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());

        services.Should().Contain(sd => sd.ServiceType == typeof(IStartupValidator)
            && sd.ImplementationInstance is PooledTenantContextStartupValidator);
    }

    private sealed class PlainDbContext : DbContext
    {
        public PlainDbContext(DbContextOptions<PlainDbContext> options) : base(options)
        {
        }
    }

    private sealed class PooledTenantDbContext : DbContext, ITenantScopedDbContext
    {
        public PooledTenantDbContext(DbContextOptions<PooledTenantDbContext> options) : base(options)
        {
        }

        public string? CurrentTenantId => null;
    }
}
