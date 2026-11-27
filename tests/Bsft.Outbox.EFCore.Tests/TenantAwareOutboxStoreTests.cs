using Bsft.Data.MultiTenancy;
using Bsft.Outbox.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Outbox.EFCore.Tests;

/// <summary>
/// Tests the lease protocol when a row source implements <see cref="IOutboxRowScope{TEntity}"/> to
/// narrow the rows the store may see: the case of a tenant-keyed table where a pass polls and settles
/// one tenant's rows, or a pass without a tenant context that claims on the tenant of the row it read.
/// </summary>
public sealed class TenantAwareOutboxStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    private readonly TenantAwareFixture _outbox = new();
    private readonly IOutboxStore<string> _store;

    public TenantAwareOutboxStoreTests() => _store = Store(_outbox);

    public void Dispose() => _outbox.Dispose();

    [Fact]
    public async Task A_tenant_aware_source_leases_only_the_current_tenant_s_rows()
    {
        var idA = await _outbox.GivenIntentAsync("tenant-a");
        var idB = await _outbox.GivenIntentAsync("tenant-b");

        _outbox.SetCurrentTenant("tenant-a");
        var leased = await _store.LeaseAsync(Now, Lease, 10);

        leased.Should().ContainSingle();
        leased[0].Id.Should().Be(idA);
        leased[0].Payload.Should().Be("tenant-a");
    }

    [Fact]
    public async Task A_tenant_aware_source_leases_only_the_second_tenant_s_rows_when_switched()
    {
        var idA = await _outbox.GivenIntentAsync("tenant-a");
        var idB = await _outbox.GivenIntentAsync("tenant-b");

        _outbox.SetCurrentTenant("tenant-b");
        var leased = await _store.LeaseAsync(Now, Lease, 10);

        leased.Should().ContainSingle();
        leased[0].Id.Should().Be(idB);
    }

    [Fact]
    public async Task Claiming_restricts_to_the_tenant_of_the_claimed_row()
    {
        // Arrange: multiple rows across tenants
        var idA1 = await _outbox.GivenIntentAsync("tenant-a");
        var idA2 = await _outbox.GivenIntentAsync("tenant-a");
        var idB1 = await _outbox.GivenIntentAsync("tenant-b");

        // Act: claim by tenant-a leases only tenant-a's rows
        _outbox.SetCurrentTenant("tenant-a");
        var leasedA = await _store.LeaseAsync(Now, Lease, 10);

        leasedA.Should().HaveCount(2);
        leasedA.Select(r => r.Id).Should().Contain(new[] { idA1, idA2 });

        // Act: mark the first row as processed
        var markedA1 = await _store.TryMarkProcessedAsync(idA1, leasedA[0].LeaseToken, Now.AddSeconds(1));
        markedA1.Should().BeTrue();

        // Act: tenant-b should be able to claim its row independently
        _outbox.SetCurrentTenant("tenant-b");
        var leasedB = await _store.LeaseAsync(Now, Lease, 10);

        leasedB.Should().ContainSingle();
        leasedB[0].Id.Should().Be(idB1);
    }

    [Fact]
    public async Task A_lease_is_tenant_aware_and_does_not_mark_rows_of_another_tenant()
    {
        var idA = await _outbox.GivenIntentAsync("tenant-a");
        var idB = await _outbox.GivenIntentAsync("tenant-b");

        _outbox.SetCurrentTenant("tenant-a");
        var leasedA = await _store.LeaseAsync(Now, Lease, 10);

        // Try to mark tenant-b's row using tenant-a's lease token; it should fail
        var markedB = await _store.TryMarkProcessedAsync(idB, leasedA[0].LeaseToken, Now.AddSeconds(1));

        markedB.Should().BeFalse("WithOwnerOf restricts the claim to the owner of the row");
        var rowB = await _outbox.ReadRowAsync(idB);
        rowB.ProcessedAt.Should().BeNull("tenant-b's row should not be marked");
    }

    [Fact]
    public async Task Abandonment_is_scoped_to_the_tenant()
    {
        var idA = await _outbox.GivenIntentAsync("tenant-a");
        var idB = await _outbox.GivenIntentAsync("tenant-b");

        _outbox.SetCurrentTenant("tenant-a");
        var leasedA = await _store.LeaseAsync(Now, Lease, 10);

        await _store.AbandonAsync(idA, leasedA[0].LeaseToken, Now.AddMinutes(5));

        var rowA = await _outbox.ReadRowAsync(idA);
        rowA.NextAttemptAt.Should().Be(Now.AddMinutes(5).UtcDateTime);

        // Verify tenant-b's row is not affected
        var rowB = await _outbox.ReadRowAsync(idB);
        rowB.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public async Task DeadLettering_is_scoped_to_the_tenant()
    {
        var idA = await _outbox.GivenIntentAsync("tenant-a");
        var idB = await _outbox.GivenIntentAsync("tenant-b");

        _outbox.SetCurrentTenant("tenant-a");
        var leasedA = await _store.LeaseAsync(Now, Lease, 10);

        await _store.DeadLetterAsync(idA, leasedA[0].LeaseToken, Now.AddSeconds(5), "tenant-a error");

        var rowA = await _outbox.ReadRowAsync(idA);
        rowA.DeadLetteredAt.Should().Be(Now.AddSeconds(5).UtcDateTime);
        rowA.FailureReason.Should().Be("tenant-a error");

        // Verify tenant-b's row is not affected
        var rowB = await _outbox.ReadRowAsync(idB);
        rowB.DeadLetteredAt.Should().BeNull();
        rowB.FailureReason.Should().BeNull();
    }

    private static IOutboxStore<string> Store(TenantAwareFixture outbox) =>
        new EFCoreOutboxStore<TenantAwareIntentRow, string>(
            outbox.Scopes,
            new TenantAwareIntentRowSource());
}
