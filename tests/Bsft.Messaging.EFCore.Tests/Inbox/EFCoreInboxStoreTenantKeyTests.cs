using Bsft.Core.Tenancy;
using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.Inbox;

/// <summary>
/// The inbox store over a tenant-keyed <c>InboxMessages</c> table on a real PostgreSQL server: one
/// event is recorded once per tenant, a tenant does not see another's record, and a scope without a
/// tenant is refused; against the table without tenant keys the same calls behave as before.
/// </summary>
public sealed class EFCoreInboxStoreTenantKeyTests : IClassFixture<PostgresDatabaseFixture>, IAsyncLifetime
{
    private const string Consumer = "billing";
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly PostgresDatabaseFixture _fixture;
    private DbContextOptions<TestTenantKeyedDbContext>? _keyed;

    public EFCoreInboxStoreTenantKeyTests(PostgresDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _keyed = await _fixture.CreateTenantKeyedDatabaseAsync("inbox_tk");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static IntegrationEventMessage Message(Guid eventId) => new()
    {
        EventId = eventId,
        EventName = "orders.placed.v1",
        OccurredAt = Now,
        Payload = "{}",
    };

    /// <summary>Begins and commits a delivery in its own context; returns whether it was new.</summary>
    private async Task<bool> DeliverAsync(string? tenantId, Guid eventId)
    {
        await using var db = new TestTenantKeyedDbContext(_keyed!);
        var store = new EFCoreInboxStore(
            db, NullLogger<EFCoreInboxStore>.Instance, new TestTenantContext { TenantId = tenantId });

        if (!await store.TryBeginAsync(Message(eventId), Consumer, Now))
            return false;

        await store.CommitAsync();
        return true;
    }

    [PostgresFact]
    public async Task TryBeginAsync_SameEventInTwoTenants_IsNewForEach()
    {
        var eventId = Guid.NewGuid();

        (await DeliverAsync("t1", eventId)).Should().BeTrue();
        (await DeliverAsync("t2", eventId)).Should().BeTrue("the record of t1 is not t2's");

        await using var db = new TestTenantKeyedDbContext(_keyed!);
        var rows = await db.InboxMessages.AsNoTracking().Where(e => e.EventId == eventId).ToListAsync();
        rows.Select(e => e.TenantId).Should().BeEquivalentTo(["t1", "t2"]);
        rows.Select(e => e.Id).Should().OnlyHaveUniqueItems().And.AllSatisfy(id => id.Should().BePositive());
    }

    [PostgresFact]
    public async Task TryBeginAsync_SameEventTwiceInOneTenant_IsDuplicate()
    {
        var eventId = Guid.NewGuid();

        (await DeliverAsync("t1", eventId)).Should().BeTrue();
        (await DeliverAsync("t1", eventId)).Should().BeFalse();
    }

    [PostgresFact]
    public async Task TryBeginAsync_InBackgroundTenant_UsesThatTenant()
    {
        var eventId = Guid.NewGuid();

        using (new BackgroundTenant("t5"))
            (await DeliverAsync(null, eventId)).Should().BeTrue();

        (await DeliverAsync("t5", eventId)).Should().BeFalse("the background pass recorded it for t5");
    }

    [PostgresFact]
    public async Task TryBeginAsync_WithoutTenant_Throws()
    {
        var act = () => DeliverAsync(null, Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*InboxMessages*");
    }

    [PostgresFact]
    public async Task TryBeginAsync_WithoutTenantKeys_IgnoresTenantAndNeedsNone()
    {
        var options = await _fixture.CreateDatabaseAsync("inbox_plain");
        var eventId = Guid.NewGuid();

        async Task<bool> Deliver(string? tenantId)
        {
            await using var db = new TestPostgresDbContext(options);
            var store = new EFCoreInboxStore(
                db, NullLogger<EFCoreInboxStore>.Instance, new TestTenantContext { TenantId = tenantId });
            if (!await store.TryBeginAsync(Message(eventId), Consumer, Now))
                return false;
            await store.CommitAsync();
            return true;
        }

        (await Deliver(null)).Should().BeTrue();
        (await Deliver("t2")).Should().BeFalse("without tenant keys one record covers every tenant");
    }
}
