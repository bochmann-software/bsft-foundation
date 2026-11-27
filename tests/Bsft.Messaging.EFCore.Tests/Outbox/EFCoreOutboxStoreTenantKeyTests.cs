using Bsft.Core.Tenancy;
using Bsft.Messaging.EFCore.Outbox;
using Bsft.Messaging.EFCore.Tests.Support;
using Bsft.Messaging.Outbox;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.Outbox;

/// <summary>
/// The integration-event outbox store over a tenant-keyed <c>OutboxMessages</c> table on a real
/// PostgreSQL server: a pass inside a <see cref="BackgroundTenant"/> touches its tenant's rows only, and
/// a pass without a tenant settles exactly the row it claimed although another tenant's row shares its id.
/// </summary>
public sealed class EFCoreOutboxStoreTenantKeyTests : IClassFixture<PostgresDatabaseFixture>, IAsyncLifetime
{
    private const long SharedId = 42;
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    private readonly PostgresDatabaseFixture _fixture;
    private ServiceProvider? _provider;

    public EFCoreOutboxStoreTenantKeyTests(PostgresDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var options = await _fixture.CreateTenantKeyedDatabaseAsync("outbox_tk");

        var services = new ServiceCollection();
        services.AddScoped(_ => new TestTenantKeyedDbContext(options));
        services.AddScoped<IOutboxDbContext>(sp => sp.GetRequiredService<TestTenantKeyedDbContext>());
        _provider = services.BuildServiceProvider();

        await using var db = new TestTenantKeyedDbContext(options);
        db.OutboxMessages.AddRange(Row("t1"), Row("t2"));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
    }

    private static OutboxMessageEntity Row(string tenantId) => new()
    {
        TenantId = tenantId,
        Id = SharedId,
        EventId = Guid.NewGuid(),
        EventTypeName = "orders.placed.v1",
        PayloadJson = """{"orderNumber":"A-1"}""",
        OccurredAt = Now.UtcDateTime,
    };

    private IOutboxStore Store() =>
        new EFCoreOutboxStore(
            new global::Bsft.Outbox.EFCore.EFCoreOutboxStore<OutboxMessageEntity, IntegrationEventMessage>(
                _provider!.GetRequiredService<IServiceScopeFactory>(), new OutboxMessageRowSource()));

    private async Task<OutboxMessageEntity> ReadAsync(string tenantId)
    {
        using var scope = _provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestTenantKeyedDbContext>();

        return await db.OutboxMessages.AsNoTracking().SingleAsync(e => e.TenantId == tenantId && e.Id == SharedId);
    }

    [PostgresFact]
    public async Task LeaseAsync_InTenantPass_ClaimsOnlyThatTenantsRow()
    {
        IReadOnlyList<LeasedOutboxMessage> leased;
        using (new BackgroundTenant("t1"))
            leased = await Store().LeaseAsync(Now, Lease, batchSize: 10);

        leased.Should().ContainSingle().Which.Id.Should().Be(SharedId);
        (await ReadAsync("t1")).LeaseToken.Should().Be(leased[0].LeaseToken);
        (await ReadAsync("t2")).LeaseToken.Should().BeNull("the other tenant's row with the same id is not this pass's");
    }

    [PostgresFact]
    public async Task LeaseAsync_WithoutTenant_ClaimsBothRowsSharingAnId()
    {
        var leased = await Store().LeaseAsync(Now, Lease, batchSize: 10);

        leased.Should().HaveCount(2);
        leased.Select(row => row.Id).Should().AllBeEquivalentTo(SharedId);
        leased.Select(row => row.LeaseToken).Should().OnlyHaveUniqueItems();
        (await ReadAsync("t1")).LeaseToken.Should().NotBeNull();
        (await ReadAsync("t2")).LeaseToken.Should().NotBeNull();
    }

    [PostgresFact]
    public async Task TryMarkProcessedAsync_InTenantPass_MarksOnlyThatTenantsRow()
    {
        using (new BackgroundTenant("t1"))
        {
            var store = Store();
            var leased = await store.LeaseAsync(Now, Lease, batchSize: 10);

            (await store.TryMarkProcessedAsync(SharedId, leased.Single().LeaseToken, Now)).Should().BeTrue();
        }

        (await ReadAsync("t1")).ProcessedAt.Should().NotBeNull();
        (await ReadAsync("t2")).ProcessedAt.Should().BeNull();
    }

    [PostgresFact]
    public async Task TryMarkProcessedAsync_WithoutTenant_MarksExactlyTheClaimedRow()
    {
        var store = Store();
        var leased = await store.LeaseAsync(Now, Lease, batchSize: 10);
        var first = leased[0];

        (await store.TryMarkProcessedAsync(SharedId, first.LeaseToken, Now)).Should().BeTrue();

        var t1 = await ReadAsync("t1");
        var t2 = await ReadAsync("t2");
        var (marked, other) = t1.LeaseToken == first.LeaseToken ? (t1, t2) : (t2, t1);

        marked.ProcessedAt.Should().NotBeNull();
        other.ProcessedAt.Should().BeNull("the row of the other tenant shares the id but not the lease token");
    }

    [PostgresFact]
    public async Task AbandonAsync_InTenantPass_LeavesOtherTenantsRowUntouched()
    {
        using (new BackgroundTenant("t1"))
        {
            var store = Store();
            var leased = await store.LeaseAsync(Now, Lease, batchSize: 10);
            await store.AbandonAsync(SharedId, leased.Single().LeaseToken, Now.AddMinutes(5));
        }

        var t1 = await ReadAsync("t1");
        t1.AttemptCount.Should().Be(1);
        t1.NextAttemptAt.Should().NotBeNull();

        var t2 = await ReadAsync("t2");
        t2.AttemptCount.Should().Be(0);
        t2.NextAttemptAt.Should().BeNull();
    }

    [PostgresFact]
    public async Task DeadLetterAsync_InTenantPass_LeavesOtherTenantsRowUntouched()
    {
        using (new BackgroundTenant("t2"))
        {
            var store = Store();
            var leased = await store.LeaseAsync(Now, Lease, batchSize: 10);
            await store.DeadLetterAsync(SharedId, leased.Single().LeaseToken, Now, "boom");
        }

        (await ReadAsync("t2")).DeadLetteredAt.Should().NotBeNull();
        (await ReadAsync("t1")).DeadLetteredAt.Should().BeNull();
    }

    [PostgresFact]
    public async Task SaveChanges_InTenantPass_FillsTimeOrderedIdAndTenant()
    {
        OutboxMessageEntity first, second;
        using (new BackgroundTenant("t3"))
        using (var scope = _provider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestTenantKeyedDbContext>();
            first = new OutboxMessageEntity { EventId = Guid.NewGuid(), EventTypeName = "e", PayloadJson = "{}", OccurredAt = Now.UtcDateTime };
            second = new OutboxMessageEntity { EventId = Guid.NewGuid(), EventTypeName = "e", PayloadJson = "{}", OccurredAt = Now.UtcDateTime };
            db.OutboxMessages.AddRange(first, second);
            await db.SaveChangesAsync();
        }

        first.TenantId.Should().Be("t3");
        second.TenantId.Should().Be("t3");
        first.Id.Should().BePositive();
        second.Id.Should().BeGreaterThan(first.Id);
    }

    [PostgresFact]
    public async Task SaveChanges_WithoutTenant_Throws()
    {
        using var scope = _provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestTenantKeyedDbContext>();

        var act = () => db.OutboxMessages.Add(
            new OutboxMessageEntity { EventId = Guid.NewGuid(), EventTypeName = "e", PayloadJson = "{}", OccurredAt = Now.UtcDateTime });

        act.Should().Throw<InvalidOperationException>().WithMessage("*OutboxMessages*");
    }
}
