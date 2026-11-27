using Bsft.Core.Tenancy;
using Bsft.Outbox.EFCore.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Outbox.EFCore.Tests;

/// <summary>
/// Inside a tenant's pass the store opens every operation's scope through
/// <see cref="ITenantScopeFactory.CreateScopeAsync"/> and disposes it asynchronously; outside one, and without a
/// factory, it opens a plain scope as before.
/// </summary>
public sealed class EFCoreOutboxStoreTenantScopeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    private readonly ProvisioningFixture _outbox = new();

    public void Dispose() => _outbox.Dispose();

    [Fact]
    public async Task Inside_a_tenant_pass_every_operation_binds_the_tenant_asynchronously()
    {
        var processedId = await _outbox.GivenIntentAsync("first");
        await _outbox.GivenIntentAsync("second");
        await _outbox.GivenIntentAsync("third");
        var tenantScopes = new AsyncOnlyTenantScopeFactory(_outbox.Scopes);
        var store = Store(tenantScopes);

        using (new BackgroundTenant("a"))
        {
            var leased = await store.LeaseAsync(Now, Lease, 10);
            leased.Should().HaveCount(3);

            (await store.TryMarkProcessedAsync(leased[0].Id, leased[0].LeaseToken, Now)).Should().BeTrue();
            await store.AbandonAsync(leased[1].Id, leased[1].LeaseToken, Now + Lease);
            await store.DeadLetterAsync(leased[2].Id, leased[2].LeaseToken, Now, "poison");
        }

        tenantScopes.BoundFor.Should().Equal("a", "a", "a", "a");
        tenantScopes.AsyncDisposals.Should().Be(4);
        (await _outbox.ReadRowAsync(processedId)).ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Outside_a_tenant_pass_the_factory_is_not_asked()
    {
        await _outbox.GivenIntentAsync("acme");
        var tenantScopes = new AsyncOnlyTenantScopeFactory(_outbox.Scopes);

        var leased = await Store(tenantScopes).LeaseAsync(Now, Lease, 10);

        leased.Should().ContainSingle();
        tenantScopes.BoundFor.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_factory_a_tenant_pass_uses_a_plain_scope()
    {
        await _outbox.GivenIntentAsync("acme");
        var store = new EFCoreOutboxStore<ProvisioningIntentRow, string>(_outbox.Scopes, new ProvisioningIntentRowSource());

        using (new BackgroundTenant("a"))
            (await store.LeaseAsync(Now, Lease, 10)).Should().ContainSingle();
    }

    [Fact]
    public void The_constructor_rejects_a_missing_tenant_scope_factory()
    {
        var act = () => Store(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tenantScopes");
    }

    private EFCoreOutboxStore<ProvisioningIntentRow, string> Store(ITenantScopeFactory tenantScopes) =>
        new(_outbox.Scopes, new ProvisioningIntentRowSource(), tenantScopes);
}
