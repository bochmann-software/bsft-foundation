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
/// Tests for tenant key registration startup validation, ensuring that contexts mapped with
/// UseTenantKeys have AddBsftDataTenantKeys called in the same host.
/// </summary>
public sealed class TenantKeyRegistrationStartupValidatorTests
{
    [Fact]
    public void Constructor_NullServices_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();

        var act = () => new TenantKeyRegistrationStartupValidator(null!, scopes);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void Constructor_NullScopes_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => new TenantKeyRegistrationStartupValidator(services, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("scopes");
    }

    [Fact]
    public async Task ValidateAsync_TenantKeyedContextWithoutAddBsftDataTenantKeys_Fails()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        // Intentionally NOT calling AddBsftDataTenantKeys
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var validator = new TenantKeyRegistrationStartupValidator(services, scopes);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain(nameof(TestDbContextWithTenantKeys))
            .And.Contain("UseTenantKeys")
            .And.Contain("AddBsftDataTenantKeys");
    }

    [Fact]
    public async Task ValidateAsync_TenantKeyedContextWithAddBsftDataTenantKeys_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var validator = new TenantKeyRegistrationStartupValidator(services, scopes);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_NonTenantKeyedContextWithoutAddBsftDataTenantKeys_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithoutTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        // Intentionally NOT calling AddBsftDataTenantKeys
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var validator = new TenantKeyRegistrationStartupValidator(services, scopes);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_TenantKeyedContextViaAddPooledDbContextFactory_Fails()
    {
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<TenantKeyTestDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        // Intentionally NOT calling AddBsftDataTenantKeys
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var validator = new TenantKeyRegistrationStartupValidator(services, scopes);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain(nameof(TenantKeyTestDbContext))
            .And.Contain("UseTenantKeys")
            .And.Contain("AddBsftDataTenantKeys");
    }

    [Fact]
    public async Task ValidateAsync_ContextConstructionThrowsInvalidOperationException_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ThrowingInvalidOperationContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        // Intentionally NOT calling AddBsftDataTenantKeys
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var validator = new TenantKeyRegistrationStartupValidator(services, scopes);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_ContextConstructionThrowsArgumentException_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddDbContextPool<ThrowingArgumentExceptionContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        // Intentionally NOT calling AddBsftDataTenantKeys
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var validator = new TenantKeyRegistrationStartupValidator(services, scopes);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_TwoTenantKeyedContextsWithoutAddBsftDataTenantKeys_FailsWithTwoErrors()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddDbContext<TenantKeyTestDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        // Intentionally NOT calling AddBsftDataTenantKeys
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var validator = new TenantKeyRegistrationStartupValidator(services, scopes);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Satisfy(
            error => error.Contains(nameof(TestDbContextWithTenantKeys)),
            error => error.Contains(nameof(TenantKeyTestDbContext)));
    }

    [Fact]
    public async Task ValidateAsync_CancelledToken_ReturnsCancelledTask()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var validator = new TenantKeyRegistrationStartupValidator(services, scopes);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var task = validator.ValidateAsync(cts.Token);

        task.IsCanceled.Should().BeTrue();
        var act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void AddBsftDataMultiTenancy_RegistersValidatorOnce()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());

        var validatorRegistrations = services
            .Where(sd =>
                sd.ServiceType == typeof(Bsft.Core.IStartupValidator)
                && sd.ImplementationType == typeof(TenantKeyRegistrationStartupValidator))
            .ToList();

        validatorRegistrations.Should().HaveCount(1);
    }

    [Fact]
    public void AddBsftDataMultiTenancy_CalledTwice_RegistersValidatorOnce()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());

        var validatorRegistrations = services
            .Where(sd =>
                sd.ServiceType == typeof(Bsft.Core.IStartupValidator)
                && sd.ImplementationType == typeof(TenantKeyRegistrationStartupValidator))
            .ToList();

        validatorRegistrations.Should().HaveCount(1);
    }

    [Fact]
    public void AddBsftDataTenantKeys_RegistersMarker()
    {
        var services = new ServiceCollection();

        services.AddBsftDataTenantKeys(o => o.NodeId = 1);

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(TenantKeyRegistration));
    }

    [Fact]
    public async Task ValidateAsync_RegistrationMarkerPresent_ImmediatelySucceeds()
    {
        var services = new ServiceCollection();
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        // Add a tenant-keyed context, but since the marker is present, it should succeed without checking it
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var validator = new TenantKeyRegistrationStartupValidator(services, scopes);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        // The message should indicate that tenant keys are registered
        result.Message.Should().Contain("AddBsftDataTenantKeys");
    }

    /// <summary>
    /// A context that throws InvalidOperationException during construction, mapped with tenant keys.
    /// </summary>
    private sealed class ThrowingInvalidOperationContext : DbContext
    {
        public ThrowingInvalidOperationContext(DbContextOptions<ThrowingInvalidOperationContext> options)
            : base(options)
        {
            throw new InvalidOperationException("Intentional construction failure for testing");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseTenantKeys();
            base.OnModelCreating(modelBuilder);
        }
    }

    /// <summary>
    /// A context that throws ArgumentException during construction, mapped with tenant keys.
    /// </summary>
    private sealed class ThrowingArgumentExceptionContext : DbContext
    {
        public ThrowingArgumentExceptionContext(DbContextOptions<ThrowingArgumentExceptionContext> options)
            : base(options)
        {
            throw new ArgumentException("Intentional argument exception for testing");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseTenantKeys();
            base.OnModelCreating(modelBuilder);
        }
    }
}
