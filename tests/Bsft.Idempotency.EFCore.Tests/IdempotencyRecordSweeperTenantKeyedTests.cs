using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Bsft.Idempotency.EFCore.Tests.Fakes;
using Bsft.Idempotency.EFCore.Tests.Support;
using Bsft.Core.Tenancy;
using Bsft.Testing.Contexts;
using Bsft.Data.MultiTenancy;
using Bsft.Tests.Shared;

namespace Bsft.Idempotency.EFCore.Tests;

public sealed class IdempotencyRecordSweeperTenantKeyedTests : IDisposable
{
    private const string Scope = "scope_acme";

    /// <summary>What the pruner bounds one statement at, mirrored so the test says what it asserts.</summary>
    private const int BatchSize = 500;

    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgresDatabaseFixture _postgresKeyed = new();
    private readonly SqliteFixture _sqliteUnkeyed = new();
    private readonly MutableTimeProvider _time = new(Now);
    private readonly ServiceProvider _keyedProvider;
    private readonly ServiceProvider _unkeyedProvider;

    public IdempotencyRecordSweeperTenantKeyedTests()
    {
        var keyedServices = new ServiceCollection();
        keyedServices.AddScoped<IIdempotencyDbContext>(_ => _postgresKeyed.NewContext());
        keyedServices.AddScoped<ITenantContext>(sp => new TestTenantContext(BackgroundTenant.Current ?? "test-tenant"));

        _keyedProvider = keyedServices.BuildServiceProvider();

        var unkeyedServices = new ServiceCollection();
        unkeyedServices.AddScoped<IIdempotencyDbContext>(_ => _sqliteUnkeyed.NewContext());

        _unkeyedProvider = unkeyedServices.BuildServiceProvider();
    }

    public void Dispose()
    {
        _unkeyedProvider.Dispose();
        _keyedProvider.Dispose();
        _sqliteUnkeyed.Dispose();
        _postgresKeyed.Dispose();
    }

    private IdempotencyRecordSweeper KeyedSweeper() =>
        new(_keyedProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BsftIdempotencyOptions { Retention = TimeSpan.FromHours(24) }),
            _time,
            NullLogger<IdempotencyRecordSweeper>.Instance,
            new FakeBackgroundTenantSource(["tenant-a", "tenant-b"]));

    private IdempotencyRecordSweeper UnkeyedSweeper() =>
        new(_unkeyedProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BsftIdempotencyOptions { Retention = TimeSpan.FromHours(24) }),
            _time,
            NullLogger<IdempotencyRecordSweeper>.Instance);

    /// <summary>Seeds an expired record for the given tenant.</summary>
    private async Task RecordAsync(string tenantId, string key, IdempotencyRecordState state, TimeSpan expiresIn)
    {
        await using var context = _postgresKeyed.NewContext();

        context.IdempotencyRecords.Add(new IdempotencyRecordEntity
        {
            TenantId = tenantId,
            Scope = Scope,
            Key = key,
            Fingerprint = "fp",
            State = state,
            ExpiresAt = (Now + expiresIn).UtcDateTime,
        });

        await context.SaveChangesAsync();
    }

    /// <summary>Seeds an unkeyed record.</summary>
    private async Task UnkeyedRecordAsync(string key, IdempotencyRecordState state, TimeSpan expiresIn)
    {
        await using var context = _sqliteUnkeyed.NewContext();

        context.IdempotencyRecords.Add(new IdempotencyRecordEntity
        {
            Scope = Scope,
            Key = key,
            Fingerprint = "fp",
            State = state,
            ExpiresAt = (Now + expiresIn).UtcDateTime,
        });

        await context.SaveChangesAsync();
    }

    private async Task<List<IdempotencyRecordEntity>> ReadKeyedRecordsAsync(string tenantId)
    {
        await using var context = _postgresKeyed.NewContext();

        return await context.IdempotencyRecords
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId)
            .OrderBy(entity => entity.Id)
            .ToListAsync();
    }

    private async Task<List<IdempotencyRecordEntity>> ReadAllKeyedRecordsAsync()
    {
        await using var context = _postgresKeyed.NewContext();

        return await context.IdempotencyRecords
            .AsNoTracking()
            .OrderBy(entity => entity.TenantId)
            .ThenBy(entity => entity.Id)
            .ToListAsync();
    }

    private async Task<List<IdempotencyRecordEntity>> ReadUnkeyedRecordsAsync()
    {
        await using var context = _sqliteUnkeyed.NewContext();

        return await context.IdempotencyRecords.AsNoTracking().OrderBy(entity => entity.Id).ToListAsync();
    }

    [PostgresFact]
    public async Task InsideBackgroundTenant_PurgeOnceAsync_DeletesOnlyCurrentTenantExpiredRows()
    {
        await RecordAsync("tenant-a", "key_a_stale", IdempotencyRecordState.Completed, -TimeSpan.FromHours(1));
        await RecordAsync("tenant-a", "key_a_fresh", IdempotencyRecordState.Completed, TimeSpan.FromHours(1));
        await RecordAsync("tenant-b", "key_b_stale", IdempotencyRecordState.Completed, -TimeSpan.FromHours(1));
        await RecordAsync("tenant-b", "key_b_fresh", IdempotencyRecordState.Completed, TimeSpan.FromHours(1));

        // Sweep for tenant A only.
        using (new BackgroundTenant("tenant-a"))
        {
            var sweeper = KeyedSweeper();
            var pruned = await sweeper.PurgeOnceAsync();

            // Only A's expired record should be deleted.
            pruned.Should().Be(1);
        }

        // Verify A lost only its stale record.
        var recordsA = await ReadKeyedRecordsAsync("tenant-a");
        recordsA.Should().ContainSingle(r => r.Key == "key_a_fresh");

        // Verify B still has both records.
        var recordsB = await ReadKeyedRecordsAsync("tenant-b");
        recordsB.Should().HaveCount(2);
    }

    [PostgresFact]
    public async Task TenantlessSweeperWithPerTenantSource_DeletesOnlyMatchingTenantRows()
    {
        // Seed both tenants with expired records, using the same ID pattern to check scoping.
        await RecordAsync("tenant-a", "key_5_a", IdempotencyRecordState.Completed, -TimeSpan.FromHours(1));
        await RecordAsync("tenant-b", "key_5_b", IdempotencyRecordState.Completed, TimeSpan.FromHours(1));

        var sweeper = KeyedSweeper();
        var prunedA = await RunTenantPassAsync(sweeper, "tenant-a");
        var prunedB = await RunTenantPassAsync(sweeper, "tenant-b");

        // Only A's expired record was deleted.
        prunedA.Should().Be(1);
        prunedB.Should().Be(0);

        var recordsA = await ReadKeyedRecordsAsync("tenant-a");
        var recordsB = await ReadKeyedRecordsAsync("tenant-b");

        recordsA.Should().BeEmpty();
        recordsB.Should().ContainSingle();
    }

    [Fact]
    public async Task WithoutTenantKeys_ExpiredRowsFromBothTenantsCanBeDeletedTogether_Gegenprobe()
    {
        await UnkeyedRecordAsync("key_a_stale", IdempotencyRecordState.Completed, -TimeSpan.FromHours(1));
        await UnkeyedRecordAsync("key_b_stale", IdempotencyRecordState.Completed, -TimeSpan.FromHours(1));
        await UnkeyedRecordAsync("key_fresh", IdempotencyRecordState.Completed, TimeSpan.FromHours(1));

        var sweeper = UnkeyedSweeper();
        var pruned = await sweeper.PurgeOnceAsync();

        // Both stale records are deleted in one pass (no tenant separation).
        pruned.Should().Be(2);

        var remaining = await ReadUnkeyedRecordsAsync();
        remaining.Should().ContainSingle(r => r.Key == "key_fresh");
    }

    [PostgresFact]
    public async Task MultipleTenantsWithMixedExpiry_OnlyCurrentTenantExpiredRowsDeleted()
    {
        await RecordAsync("tenant-a", "a1", IdempotencyRecordState.Completed, -TimeSpan.FromHours(1));
        await RecordAsync("tenant-a", "a2", IdempotencyRecordState.Completed, TimeSpan.FromHours(1));
        await RecordAsync("tenant-a", "a3", IdempotencyRecordState.Completed, -TimeSpan.FromHours(2));

        await RecordAsync("tenant-b", "b1", IdempotencyRecordState.Completed, -TimeSpan.FromHours(1));
        await RecordAsync("tenant-b", "b2", IdempotencyRecordState.Completed, -TimeSpan.FromHours(1));

        using (new BackgroundTenant("tenant-a"))
        {
            var sweeper = KeyedSweeper();
            var prunedA = await sweeper.PurgeOnceAsync();
            prunedA.Should().Be(2); // a1 and a3 are expired.
        }

        var recordsA = await ReadKeyedRecordsAsync("tenant-a");
        var recordsB = await ReadKeyedRecordsAsync("tenant-b");

        recordsA.Should().ContainSingle(r => r.Key == "a2");
        recordsB.Should().HaveCount(2); // Untouched.
    }

    [PostgresFact]
    public async Task FreshReservedRecords_AreNotDeleted_TenantKeyed()
    {
        // Reserved records stay standing while the lease is good and should never be pruned.
        await RecordAsync("tenant-a", "key_reserved", IdempotencyRecordState.Reserved, TimeSpan.FromHours(1)); // Fresh, not expired

        using (new BackgroundTenant("tenant-a"))
        {
            var sweeper = KeyedSweeper();
            var pruned = await sweeper.PurgeOnceAsync();
            pruned.Should().Be(0);
        }

        var records = await ReadKeyedRecordsAsync("tenant-a");
        records.Should().ContainSingle(r => r.Key == "key_reserved");
    }

    /// <summary>Runs a single pass for the given tenant, using the background tenant source.</summary>
    private async Task<int> RunTenantPassAsync(IdempotencyRecordSweeper sweeper, string tenantId)
    {
        using (new BackgroundTenant(tenantId))
        {
            return await sweeper.PurgeOnceAsync();
        }
    }
}
