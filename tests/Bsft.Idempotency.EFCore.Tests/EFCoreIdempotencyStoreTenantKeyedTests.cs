using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Bsft.Idempotency.EFCore.Tests.Fakes;
using Bsft.Idempotency.EFCore.Tests.Support;
using Bsft.Testing.Contexts;
using Bsft.Core.Tenancy;
using Bsft.Tests.Shared;

namespace Bsft.Idempotency.EFCore.Tests;

public sealed class EFCoreIdempotencyStoreTenantKeyedTests : IDisposable
{
    private const string Scope = "scope_acme";
    private const string Key = "key_2f1c9a";
    private const string Fingerprint = "fp_first";
    private const string OtherFingerprint = "fp_other";

    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private readonly SqliteFixture _sqliteUnkeyed = new();
    private readonly PostgresDatabaseFixture _postgresKeyed = new();
    private readonly MutableTimeProvider _time = new(Now);
    private readonly List<TenantKeyedTestIdempotencyDbContext> _keyedContexts = [];
    private readonly List<TestIdempotencyDbContext> _unkeyedContexts = [];

    public void Dispose()
    {
        foreach (var context in _keyedContexts)
            context.Dispose();
        foreach (var context in _unkeyedContexts)
            context.Dispose();

        _postgresKeyed.Dispose();
        _sqliteUnkeyed.Dispose();
    }

    private TenantKeyedTestIdempotencyDbContext KeyedContext()
    {
        var context = _postgresKeyed.NewContext();
        _keyedContexts.Add(context);
        return context;
    }

    private TestIdempotencyDbContext UnkeyedContext()
    {
        var context = _sqliteUnkeyed.NewContext();
        _unkeyedContexts.Add(context);
        return context;
    }

    private EFCoreIdempotencyStore KeyedStore(
        string tenantId,
        IdempotencyMode mode = IdempotencyMode.Detached) =>
        new(
            KeyedContext(),
            Options.Create(new BsftIdempotencyOptions { Mode = mode }),
            _time,
            new TestTenantContext(tenantId));

    private EFCoreIdempotencyStore UnkeyedStore(
        IdempotencyMode mode = IdempotencyMode.Detached) =>
        new(
            UnkeyedContext(),
            Options.Create(new BsftIdempotencyOptions { Mode = mode }),
            _time);

    private IdempotencyReservationRequest Reservation(string fingerprint = Fingerprint, string key = Key) =>
        new(Scope, key, fingerprint, _time.GetUtcNow() + Lease);

    private IdempotencyCompletionRequest Completion(
        string fingerprint = Fingerprint,
        int statusCode = 201,
        string body = "{\"id\":1}",
        string key = Key) =>
        new(
            Scope,
            key,
            fingerprint,
            new IdempotencyResponseSnapshot(
                statusCode,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Location"] = "/orders/1",
                    ["Content-Type"] = "application/json",
                },
                Encoding.UTF8.GetBytes(body)),
            _time.GetUtcNow() + Retention);

    private async Task<List<IdempotencyRecordEntity>> ReadKeyedRecordsAsync(string tenantId)
    {
        await using var context = KeyedContext();
        var tenantCtx = new TestTenantContext(tenantId);

        return await context.IdempotencyRecords
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId)
            .OrderBy(entity => entity.Id)
            .ToListAsync();
    }

    private async Task<List<IdempotencyRecordEntity>> ReadUnkeyedRecordsAsync()
    {
        await using var context = UnkeyedContext();

        return await context.IdempotencyRecords.AsNoTracking().OrderBy(entity => entity.Id).ToListAsync();
    }

    [PostgresFact]
    public async Task TwoTenants_WithSameScopeAndKey_AreIndependent_Detached()
    {
        var storeA = KeyedStore("tenant-a", IdempotencyMode.Detached);
        var storeB = KeyedStore("tenant-b", IdempotencyMode.Detached);

        var resultA = await storeA.BeginAsync(Reservation());
        var resultB = await storeB.BeginAsync(Reservation());

        resultA.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
        resultB.Outcome.Should().Be(IdempotencyBeginOutcome.Started);

        var recordsA = await ReadKeyedRecordsAsync("tenant-a");
        var recordsB = await ReadKeyedRecordsAsync("tenant-b");

        recordsA.Should().ContainSingle(r => r.Scope == Scope && r.Key == Key);
        recordsB.Should().ContainSingle(r => r.Scope == Scope && r.Key == Key);
        recordsA[0].TenantId.Should().Be("tenant-a");
        recordsB[0].TenantId.Should().Be("tenant-b");
        recordsA[0].Id.Should().NotBe(recordsB[0].Id);
    }

    [PostgresFact]
    public async Task TwoTenants_WithSameScopeAndKey_AreIndependent_Enlisted()
    {
        var storeA = KeyedStore("tenant-a", IdempotencyMode.Enlisted);
        var storeB = KeyedStore("tenant-b", IdempotencyMode.Enlisted);

        var resultA = await storeA.BeginAsync(Reservation());
        var resultB = await storeB.BeginAsync(Reservation());

        resultA.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
        resultB.Outcome.Should().Be(IdempotencyBeginOutcome.Started);

        // Even though the contexts are not yet saved, both tenants should see Started.
        resultA.Outcome.Should().NotBe(IdempotencyBeginOutcome.InProgress);
        resultB.Outcome.Should().NotBe(IdempotencyBeginOutcome.InProgress);
    }

    [PostgresFact]
    public async Task CompletionInTenantA_DoesNotAffectTenantB_Detached()
    {
        var storeA = KeyedStore("tenant-a", IdempotencyMode.Detached);
        var storeB = KeyedStore("tenant-b", IdempotencyMode.Detached);

        await storeA.BeginAsync(Reservation());
        await storeB.BeginAsync(Reservation());

        // Complete in tenant A.
        await storeA.CompleteAsync(Completion());

        // Query tenant B should still show Reserved.
        var recordsB = await ReadKeyedRecordsAsync("tenant-b");
        recordsB.Should().ContainSingle(r => r.State == IdempotencyRecordState.Reserved);

        // Replay in tenant B should show InProgress, not Replayed.
        var replayB = await storeB.BeginAsync(Reservation());
        replayB.Outcome.Should().Be(IdempotencyBeginOutcome.InProgress);
    }

    [PostgresFact]
    public async Task ReleaseInTenantA_FreesOnlyAsRow_Detached()
    {
        var storeA = KeyedStore("tenant-a", IdempotencyMode.Detached);
        var storeB = KeyedStore("tenant-b", IdempotencyMode.Detached);

        await storeA.BeginAsync(Reservation());
        await storeB.BeginAsync(Reservation());

        // Release in tenant A.
        await storeA.ReleaseAsync(Scope, Key, Fingerprint);

        var recordsA = await ReadKeyedRecordsAsync("tenant-a");
        var recordsB = await ReadKeyedRecordsAsync("tenant-b");

        recordsA.Should().BeEmpty();
        recordsB.Should().ContainSingle();
    }

    [PostgresFact]
    public async Task LapsedReservationInA_DoesNotAffectBsRow_Detached()
    {
        var storeA = KeyedStore("tenant-a", IdempotencyMode.Detached);
        var storeB = KeyedStore("tenant-b", IdempotencyMode.Detached);

        var resultA = await storeA.BeginAsync(Reservation());
        await storeB.BeginAsync(Reservation());

        // Get the ID of B's row before advancing time.
        var recordsB = await ReadKeyedRecordsAsync("tenant-b");
        var idB = recordsB.Single().Id;

        // Advance time past A's lease.
        _time.Advance(Lease + TimeSpan.FromSeconds(1));

        // Reclaim in A.
        var reclaimA = await storeA.BeginAsync(Reservation(OtherFingerprint));
        reclaimA.Outcome.Should().Be(IdempotencyBeginOutcome.Started);

        // B's row should still have its original ID and be unchanged.
        var recordsBAfter = await ReadKeyedRecordsAsync("tenant-b");
        recordsBAfter.Should().ContainSingle(r => r.Id == idB);
    }

    [PostgresFact]
    public async Task WithoutTenantContext_Throws_OnRecordsQuery()
    {
        // Create a context without a tenant context, trying to access tenant-keyed records.
        var context = KeyedContext();
        var store = new EFCoreIdempotencyStore(
            context,
            Options.Create(new BsftIdempotencyOptions { Mode = IdempotencyMode.Detached }),
            _time,
            tenantContext: null);

        var act = async () => await store.BeginAsync(Reservation());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IdempotencyRecords*");
    }

    [Fact]
    public async Task WithoutTenantKeys_SecondTenantShowsInProgress_Gegenprobe()
    {
        var storeA = UnkeyedStore(IdempotencyMode.Detached);
        var storeB = UnkeyedStore(IdempotencyMode.Detached);

        // Without tenant keys, both stores share the same record.
        var resultA = await storeA.BeginAsync(Reservation());
        resultA.Outcome.Should().Be(IdempotencyBeginOutcome.Started);

        var resultB = await storeB.BeginAsync(Reservation());

        // B should see the same record as A, so it shows InProgress.
        resultB.Outcome.Should().Be(IdempotencyBeginOutcome.InProgress);
    }

    [PostgresFact]
    public async Task MultipleReservationsInOneTenant_AreIndependent_Detached()
    {
        var store = KeyedStore("tenant-a", IdempotencyMode.Detached);

        var result1 = await store.BeginAsync(Reservation(key: "key_1"));
        var result2 = await store.BeginAsync(Reservation(key: "key_2"));

        result1.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
        result2.Outcome.Should().Be(IdempotencyBeginOutcome.Started);

        var records = await ReadKeyedRecordsAsync("tenant-a");
        records.Should().HaveCount(2);
        records.Select(r => r.Key).Should().Contain(new[] { "key_1", "key_2" });
    }

    [PostgresFact]
    public async Task CompleteAndReplayAcrossTenants_AreIndependent_Detached()
    {
        var storeA = KeyedStore("tenant-a", IdempotencyMode.Detached);
        var storeB = KeyedStore("tenant-b", IdempotencyMode.Detached);

        // A completes the request.
        await storeA.BeginAsync(Reservation());
        await storeA.CompleteAsync(Completion());

        // B still sees its own Reserved state, not the completion from A.
        await storeB.BeginAsync(Reservation());

        var replayB = await storeB.BeginAsync(Reservation());
        replayB.Outcome.Should().Be(IdempotencyBeginOutcome.InProgress);

        // Now A replays should get the completion.
        var replayA = await storeA.BeginAsync(Reservation());
        replayA.Outcome.Should().Be(IdempotencyBeginOutcome.Replayed);
        replayA.Response.Should().NotBeNull();
    }
}
