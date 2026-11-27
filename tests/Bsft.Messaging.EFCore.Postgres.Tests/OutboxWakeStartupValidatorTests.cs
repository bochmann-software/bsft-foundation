using IStartupValidator = Bsft.Core.IStartupValidator;
using Bsft.Core.Tenancy;
using Bsft.Messaging.EFCore.Postgres.DependencyInjection;
using Bsft.Messaging.EFCore.Postgres.Tests.Fakes;
using Bsft.Messaging.EFCore.Postgres.Tests.Support;
using Bsft.Messaging.Outbox;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Messaging.EFCore.Postgres.Tests;

/// <summary>
/// The start-up warning for a silo host that keeps the listener wake: one LISTEN connection per
/// tenant database is what <see cref="BsftMessagingPostgresOptions.IsInProcessWake"/> avoids.
/// </summary>
public class OutboxWakeStartupValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WithATenantSourceAndTheListenerWake_WarnsOnce()
    {
        var services = new ServiceCollection();
        services.AddOptions<BsftMessagingPostgresOptions>()
            .Configure(options => options.IsInProcessWake = false)
            .ValidateOnStart();
        services.AddSingleton<IBackgroundTenantSource>(
            new FakeBackgroundTenantSource(["tenant1"]));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftMessagingPostgresOptions>>();
        var validator = new OutboxWakeStartupValidator(provider.GetServices<IBackgroundTenantSource>(), options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().HaveCount(1);
        result.Warnings[0].Should().Contain(nameof(IBackgroundTenantSource));
        result.Warnings[0].Should().Contain(nameof(BsftMessagingPostgresOptions.IsInProcessWake));
    }

    [Fact]
    public async Task ValidateAsync_WithATenantSourceAndTheInProcessWake_IsSilent()
    {
        var services = new ServiceCollection();
        services.AddOptions<BsftMessagingPostgresOptions>()
            .Configure(options => options.IsInProcessWake = true)
            .ValidateOnStart();
        services.AddSingleton<IBackgroundTenantSource>(
            new FakeBackgroundTenantSource(["tenant1"]));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftMessagingPostgresOptions>>();
        var validator = new OutboxWakeStartupValidator(provider.GetServices<IBackgroundTenantSource>(), options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithoutATenantSource_IsSilent()
    {
        var services = new ServiceCollection();
        services.AddOptions<BsftMessagingPostgresOptions>()
            .Configure(options => options.IsInProcessWake = false)
            .ValidateOnStart();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftMessagingPostgresOptions>>();
        var validator = new OutboxWakeStartupValidator(provider.GetServices<IBackgroundTenantSource>(), options);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithNullTenantSources_Throws()
    {
        var options = Options.Create(new BsftMessagingPostgresOptions());

        var createValidator = () => new OutboxWakeStartupValidator(null!, options);

        createValidator.Should().Throw<ArgumentNullException>()
            .WithParameterName("tenantSources");
    }

    [Fact]
    public void Constructor_WithNullOptions_Throws()
    {
        var createValidator = () => new OutboxWakeStartupValidator([], null!);

        createValidator.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_Always_RegistersTheWakeValidator()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContext<TestOutboxDbContext>(
            options => options.UseSqlite("DataSource=:memory:"));
        services.AddSingleton<IOutboxProcessor>(new ScriptedOutboxProcessor());
        services.AddBsftMessagingEFCorePostgres<TestOutboxDbContext>();

        using var provider = services.BuildServiceProvider();

        var validators = provider.GetServices<IStartupValidator>();

        validators.Should().ContainSingle(v => v is OutboxWakeStartupValidator);
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_CalledTwice_RegistersTheWakeValidatorOnce()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContext<TestOutboxDbContext>(
            options => options.UseSqlite("DataSource=:memory:"));
        services.AddSingleton<IOutboxProcessor>(new ScriptedOutboxProcessor());
        services.AddBsftMessagingEFCorePostgres<TestOutboxDbContext>();
        services.AddBsftMessagingEFCorePostgres<TestOutboxDbContext>();

        using var provider = services.BuildServiceProvider();

        var validators = provider.GetServices<IStartupValidator>();

        validators.OfType<OutboxWakeStartupValidator>().Should().ContainSingle();
    }
}
