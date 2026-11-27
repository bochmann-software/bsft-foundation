using Bsft.Tests.Shared;
using Bsft.Core.Tenancy;
using Bsft.Idempotency.EFCore.Tests.Fakes;
using Bsft.Idempotency.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Idempotency.EFCore.Tests;

/// <summary>
/// The scope of a tenant's sweep is bound through <see cref="ITenantScopeFactory.CreateScopeAsync"/> and disposed
/// asynchronously.
/// </summary>
public sealed class IdempotencyRecordSweeperTenantScopeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);
    private readonly ManualTimeProvider _clock = new(Now);
    private readonly SqliteFixture _database = new();
    private readonly List<string?> _boundFor = [];
    private readonly ServiceProvider _provider;

    public IdempotencyRecordSweeperTenantScopeTests()
    {
        var services = new ServiceCollection();
        services.AddScoped<AsyncOnlyTenantScopeFactory.BoundTenant>();
        services.AddScoped<AsyncOnlyDisposable>();
        services.AddScoped<IIdempotencyDbContext>(sp =>
        {
            sp.GetRequiredService<AsyncOnlyDisposable>();
            lock (_boundFor)
                _boundFor.Add(sp.GetRequiredService<AsyncOnlyTenantScopeFactory.BoundTenant>().TenantId);
            return _database.NewContext();
        });

        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task RunLoopAsync_WithAnAsyncOnlyTenantScopeFactory_BindsEachTenantAsynchronouslyAndSweeps()
    {
        await RecordExpiredAsync();
        var tenantScopes = NewTenantScopes();

        await RunFirstPassAsync(CreateSweeper(tenantScopes));

        tenantScopes.BoundFor.Take(2).Should().Equal("a", "b");
        Snapshot().Take(2).Should().Equal("a", "b");
        (await CountRecordsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RunLoopAsync_WithAnAsyncOnlyTenantScopeFactory_DisposesEveryScopeAsynchronously()
    {
        var tenantScopes = NewTenantScopes();

        await RunFirstPassAsync(CreateSweeper(tenantScopes));

        tenantScopes.AsyncDisposals.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task RunLoopAsync_WhenOneTenantCannotBeBound_SweepsTheOthers()
    {
        await RecordExpiredAsync();
        var tenantScopes = NewTenantScopes();
        tenantScopes.FailingTenant = "a";

        await RunFirstPassAsync(CreateSweeper(tenantScopes));

        tenantScopes.BoundFor.Take(2).Should().Equal("a", "b");
        (await CountRecordsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PurgeOnceAsync_WithoutATenantScopeFactory_SweepsInAPlainScopeDisposedAsynchronously()
    {
        await RecordExpiredAsync();
        var sweeper = new IdempotencyRecordSweeper(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BsftIdempotencyOptions { Retention = TimeSpan.FromDays(1) }),
            _clock,
            NullLogger<IdempotencyRecordSweeper>.Instance,
            new FakeBackgroundTenantSource(["a"]));

        // The sweep resolves a service that is only IAsyncDisposable: a scope disposed synchronously throws.
        var pruned = await sweeper.PurgeOnceAsync();

        pruned.Should().Be(1);
        Snapshot().Should().Equal([null]);
    }

    [Fact]
    public void Constructor_WithNullTenantScopes_Throws()
    {
        var act = () => CreateSweeper(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tenantScopes");
    }

    private AsyncOnlyTenantScopeFactory NewTenantScopes() =>
        new(_provider.GetRequiredService<IServiceScopeFactory>());

    private IdempotencyRecordSweeper CreateSweeper(ITenantScopeFactory tenantScopes) =>
        new(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BsftIdempotencyOptions { Retention = TimeSpan.FromDays(1) }),
            _clock,
            NullLogger<IdempotencyRecordSweeper>.Instance,
            new FakeBackgroundTenantSource(["a", "b"]),
            tenantScopes);

    private async Task RecordExpiredAsync()
    {
        await using var context = _database.NewContext();

        context.IdempotencyRecords.Add(new IdempotencyRecordEntity
        {
            Scope = "scope_acme",
            Key = "lapsed",
            Fingerprint = "fp",
            State = IdempotencyRecordState.Completed,
            ExpiresAt = (Now - TimeSpan.FromHours(1)).UtcDateTime,
        });

        await context.SaveChangesAsync();
    }

    private async Task<int> CountRecordsAsync()
    {
        await using var context = _database.NewContext();

        return await context.IdempotencyRecords.CountAsync();
    }

    private async Task RunFirstPassAsync(IdempotencyRecordSweeper sweeper)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var loop = sweeper.RunLoopAsync(cts.Token);

        // The first pass is complete once the loop waits out its interval on the manual clock.
        await await Task.WhenAny(_clock.WaitForAnyTimerAsync(), loop);

        await cts.CancelAsync();
        await loop;
    }

    private List<string?> Snapshot()
    {
        lock (_boundFor)
            return [.. _boundFor];
    }
}
