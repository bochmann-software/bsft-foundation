using Bsft.Core;
using Bsft.Data.DependencyInjection;
using Bsft.Data.MultiTenancy;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

/// <summary>
/// Tests for tenant-keyed context startup validation, ensuring that tenant-keyed contexts
/// are not registered with pooling or factory registrations that would scope them outside
/// the request context.
/// </summary>
public sealed class TenantKeyStartupValidatorTests
{
    [Fact]
    public void Constructor_NullServices_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new BsftDataOptions());

        var act = () => new TenantKeyStartupValidator(null!, scopes, options);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void Constructor_NullScopes_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var options = Options.Create(new BsftDataOptions());

        var act = () => new TenantKeyStartupValidator(services, null!, options);

        act.Should().Throw<ArgumentNullException>().WithParameterName("scopes");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();

        var act = () => new TenantKeyStartupValidator(services, scopes, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task ValidateAsync_NoDbContextRegistered_SucceedsWithoutWarnings()
    {
        var services = new ServiceCollection();
        services.AddOptions<BsftDataOptions>()
            .Configure(o => o.TenantKeys.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Message.Should().Be("No tenant-keyed DbContext is created outside a scope.");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_TenantKeyedContextWithAddDbContext_SucceedsWithoutWarnings()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Message.Should().Be("No tenant-keyed DbContext is created outside a scope.");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_TenantKeyedContextWithAddDbContextPool_Fails()
    {
        var services = new ServiceCollection();
        services.AddDbContextPool<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain(nameof(TestDbContextWithTenantKeys))
            .And.Contain("AddDbContextPool");
    }

    [Fact]
    public async Task ValidateAsync_TenantKeyedContextWithAddDbContextFactory_Fails()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<TenantKeyTestDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain(nameof(TenantKeyTestDbContext))
            .And.Contain("AddDbContextPool");
    }

    [Fact]
    public async Task ValidateAsync_TenantKeyedContextWithAddPooledDbContextFactory_Fails()
    {
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain(nameof(TestDbContextWithTenantKeys))
            .And.Contain("AddDbContextPool");
    }

    [Fact]
    public async Task ValidateAsync_TenantKeyedContextWithAddDbContextAndSingletonOptions_Fails()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TenantKeyTestDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()),
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain(nameof(TenantKeyTestDbContext))
            .And.Contain("AddDbContextPool");
    }

    [Fact]
    public async Task ValidateAsync_NonTenantKeyedContextWithAddDbContextPool_SucceedsWithoutWarnings()
    {
        var services = new ServiceCollection();
        services.AddDbContextPool<TestDbContextWithoutTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_TenantKeyedContextWithoutNodeId_WarnsAboutNodeId()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(options => { });
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("node id")
            .And.Contain("random");
        // Verify the warning does NOT include context names
        result.Warnings.First().Should().NotContain(nameof(TestDbContextWithTenantKeys));
    }

    [Fact]
    public async Task ValidateAsync_TenantKeyedContextWithNodeId_DoesNotWarnAboutNodeId()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(o => o.NodeId = 42);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_NoContextsWithoutNodeId_WarnsAboutNodeId()
    {
        var services = new ServiceCollection();
        services.AddBsftDataTenantKeys(options => { });
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("node id");
        // Verify the warning appears once even with NO contexts registered
        result.Warnings.Should().HaveCount(1);
    }

    [Fact]
    public async Task ValidateAsync_ContextConstructionThrowsInvalidOperationException_WarnsAndSucceeds()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<ThrowingTenantKeyContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain(nameof(ThrowingTenantKeyContext))
            .And.Contain("could not be constructed");
    }

    [Fact]
    public async Task ValidateAsync_ContextConstructionThrowsArgumentException_WarnsAndSucceeds()
    {
        var services = new ServiceCollection();
        services.AddDbContextPool<ThrowingArgumentExceptionContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain(nameof(ThrowingArgumentExceptionContext))
            .And.Contain("could not be constructed");
    }

    [Fact]
    public async Task ValidateAsync_ContextConstructionThrowsOtherException_Throws()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<ThrowingNotSupportedExceptionContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var act = async () => await validator.ValidateAsync();

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ValidateAsync_CancelledToken_ReturnsCancelledTask()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContextWithTenantKeys>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var task = validator.ValidateAsync(cts.Token);

        task.IsCanceled.Should().BeTrue();
        var act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ValidateAsync_OptionsRegisteredWithoutContextConstruction_SucceedsWithoutWarnings()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DbContextOptionsBuilder<TestDbContextWithTenantKeys>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Message.Should().Be("No tenant-keyed DbContext is created outside a scope.");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void AddBsftDataTenantKeys_RegistersValidator()
    {
        var services = new ServiceCollection();

        services.AddBsftDataTenantKeys();

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(Bsft.Core.IStartupValidator)
            && sd.ImplementationType == typeof(TenantKeyStartupValidator));
    }

    [Fact]
    public void AddBsftDataTenantKeys_CalledTwice_RegistersValidatorOnce()
    {
        var services = new ServiceCollection();

        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        services.AddBsftDataTenantKeys(o => o.NodeId = 2);

        var validatorRegistrations = services
            .Where(sd =>
                sd.ServiceType == typeof(Bsft.Core.IStartupValidator)
                && sd.ImplementationType == typeof(TenantKeyStartupValidator))
            .ToList();

        validatorRegistrations.Should().HaveCount(1);
    }

    [Fact]
    public void AddBsftDataMultiTenancy_DoesNotRegisterValidator()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());

        var validatorRegistrations = services
            .Where(sd =>
                sd.ServiceType == typeof(Bsft.Core.IStartupValidator)
                && sd.ImplementationType == typeof(TenantKeyStartupValidator))
            .ToList();

        validatorRegistrations.Should().BeEmpty();
    }

    [Fact]
    public void AddBsftDataTenantKeysAndAddBsftDataMultiTenancy_RegisterValidatorOnce()
    {
        var services = new ServiceCollection();

        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());

        var validatorRegistrations = services
            .Where(sd =>
                sd.ServiceType == typeof(Bsft.Core.IStartupValidator)
                && sd.ImplementationType == typeof(TenantKeyStartupValidator))
            .ToList();

        validatorRegistrations.Should().HaveCount(1);
    }

    [Fact]
    public async Task ValidateAsync_TenantKeyedContextWithAddDbContextThrowsNotSupportedException_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TenantKeyContextThrowingNotSupported>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        // The context should not be constructed because it's registered with AddDbContext (scoped options),
        // so ValidateAsync should not throw even though the context's constructor throws NotSupportedException.
        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_TenantKeyedContextWithAddDbContextPoolThrowsNotSupportedException_Throws()
    {
        var services = new ServiceCollection();
        services.AddDbContextPool<TenantKeyContextThrowingNotSupported>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddBsftDataTenantKeys(o => o.NodeId = 1);
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<BsftDataOptions>>();
        var validator = new TenantKeyStartupValidator(services, scopes, options);

        // The context is registered with AddDbContextPool (singleton options), so it will be constructed,
        // and ValidateAsync should throw NotSupportedException.
        var act = async () => await validator.ValidateAsync();

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    /// <summary>
    /// A context that throws InvalidOperationException during construction.
    /// </summary>
    private sealed class ThrowingTenantKeyContext : DbContext
    {
        public ThrowingTenantKeyContext(DbContextOptions<ThrowingTenantKeyContext> options) : base(options)
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
    /// A context that throws ArgumentException during construction.
    /// </summary>
    private sealed class ThrowingArgumentExceptionContext : DbContext
    {
        public ThrowingArgumentExceptionContext(DbContextOptions<ThrowingArgumentExceptionContext> options) : base(options)
        {
            throw new ArgumentException("Intentional argument exception for testing");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseTenantKeys();
            base.OnModelCreating(modelBuilder);
        }
    }

    /// <summary>
    /// A context that throws NotSupportedException during construction.
    /// </summary>
    private sealed class ThrowingNotSupportedExceptionContext : DbContext
    {
        public ThrowingNotSupportedExceptionContext(DbContextOptions<ThrowingNotSupportedExceptionContext> options) : base(options)
        {
            throw new NotSupportedException("Intentional not supported exception for testing");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseTenantKeys();
            base.OnModelCreating(modelBuilder);
        }
    }

    /// <summary>
    /// A tenant-keyed context that throws NotSupportedException during construction.
    /// Used to test that scoped contexts are not constructed, and pooled contexts are.
    /// </summary>
    private sealed class TenantKeyContextThrowingNotSupported : DbContext
    {
        public TenantKeyContextThrowingNotSupported(DbContextOptions<TenantKeyContextThrowingNotSupported> options) : base(options)
        {
            throw new NotSupportedException("Intentional not supported exception for testing");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseTenantKeys();
            base.OnModelCreating(modelBuilder);
        }
    }
}
