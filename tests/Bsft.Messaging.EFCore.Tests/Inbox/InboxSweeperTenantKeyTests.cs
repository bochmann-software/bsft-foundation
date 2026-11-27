using Bsft.Core.Tenancy;
using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Tests.Fakes;
using Bsft.Messaging.EFCore.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.Inbox;

/// <summary>
/// The inbox sweeper over a tenant-keyed <c>InboxMessages</c> table on a real PostgreSQL server: a
/// pass inside a <see cref="BackgroundTenant"/> purges that tenant's expired records only.
/// </summary>
public sealed class InboxSweeperTenantKeyTests : IClassFixture<PostgresDatabaseFixture>, IAsyncLifetime
{
    private const string Consumer = "billing";
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly PostgresDatabaseFixture _fixture;
    private DbContextOptions<TestTenantKeyedDbContext>? _options;
    private ServiceProvider? _provider;

    public InboxSweeperTenantKeyTests(PostgresDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _options = await _fixture.CreateTenantKeyedDatabaseAsync("sweep_tk");

        var services = new ServiceCollection();
        services.AddScoped(_ => new TestTenantKeyedDbContext(_options));
        services.AddScoped<IInboxDbContext>(sp => sp.GetRequiredService<TestTenantKeyedDbContext>());
        _provider = services.BuildServiceProvider();

        // The same id in both tenants, so a delete by id alone would take the other tenant's row too.
        await using var db = new TestTenantKeyedDbContext(_options);
        db.InboxMessages.AddRange(
            Record("t1", 7, Now.AddHours(-3)),
            Record("t2", 7, Now.AddHours(-3)),
            Record("t1", 8, Now.AddMinutes(-5)));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
    }

    private static InboxMessageEntity Record(string tenantId, long id, DateTimeOffset receivedAt) => new()
    {
        TenantId = tenantId,
        Id = id,
        EventId = Guid.NewGuid(),
        Consumer = Consumer,
        EventName = "orders.placed.v1",
        ReceivedAt = receivedAt.UtcDateTime,
    };

    private InboxSweeper Sweeper() =>
        new(
            _provider!.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BsftMessagingOptions { Consumer = { ConsumerName = Consumer, InboxRetention = TimeSpan.FromHours(1) } }),
            new FixedTimeProvider(Now),
            NullLogger<InboxSweeper>.Instance);

    private async Task<List<(string? TenantId, long Id)>> RemainingAsync()
    {
        await using var db = new TestTenantKeyedDbContext(_options!);
        var rows = await db.InboxMessages.AsNoTracking().OrderBy(e => e.TenantId).ThenBy(e => e.Id).ToListAsync();
        return rows.Select(e => (e.TenantId, e.Id)).ToList();
    }

    [PostgresFact]
    public async Task PurgeOnceAsync_InTenantPass_DeletesOnlyThatTenantsExpiredRecords()
    {
        int purged;
        using (new BackgroundTenant("t1"))
            purged = await Sweeper().PurgeOnceAsync(TimeSpan.FromHours(1));

        purged.Should().Be(1);
        (await RemainingAsync()).Should().Equal(("t1", 8L), ("t2", 7L));
    }

    [PostgresFact]
    public async Task PurgeOnceAsync_WithoutTenant_DeletesEveryTenantsExpiredRecords()
    {
        var purged = await Sweeper().PurgeOnceAsync(TimeSpan.FromHours(1));

        purged.Should().Be(2);
        (await RemainingAsync()).Should().Equal(("t1", 8L));
    }
}
