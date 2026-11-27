using Bsft.Core.Tenancy;
using Bsft.Scheduling.EFCore.Tests.Fakes;
using Bsft.Scheduling.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests;

/// <summary>
/// Inside a tenant's pass the store opens every operation's scope through
/// <see cref="ITenantScopeFactory.CreateScopeAsync"/> and disposes it asynchronously; outside one, and without a
/// factory, it opens a plain scope as before.
/// </summary>
public sealed class EFCoreJobStoreTenantScopeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public EFCoreJobStoreTenantScopeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestJobDbContext>(opt => opt.UseSqlite(_connection));
        services.AddScoped<IJobDbContext>(sp => sp.GetRequiredService<TestJobDbContext>());
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestJobDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task InsideATenantPassEveryOperationBindsTheTenantAsynchronously()
    {
        var tenantScopes = new AsyncOnlyTenantScopeFactory(Scopes);
        var store = new EFCoreJobStore(Scopes, new JobRunMapper(), tenantScopes);

        using (new BackgroundTenant("a"))
        {
            var settled = await store.EnqueueAsync(Run("report"));
            (await store.TryEnqueueRecurringAsync(Run("nightly"), JobOverlapPolicy.Forbid)).Should().BeTrue();
            var claimable = await store.ListClaimableAsync(Now, 10);
            claimable.Should().HaveCount(2);

            var claimed = await store.ClaimAsync(claimable.Single(run => run.Id == settled), "worker-a", Now, Now.AddMinutes(1));
            await store.SettleAsync(claimed!, JobRunStatus.Completed, Now);

            var retried = claimable.Single(run => run.Id != settled);
            var claimedAgain = await store.ClaimAsync(retried, "worker-a", Now, Now.AddMinutes(1));
            await store.RetryAsync(claimedAgain!, Now.AddMinutes(5));

            (await store.FindAsync(settled)).Should().NotBeNull();
            (await store.CancelAsync(retried.Id)).Should().BeTrue();
        }

        tenantScopes.BoundFor.Should().HaveCount(9).And.OnlyContain(tenant => tenant == "a");
        tenantScopes.AsyncDisposals.Should().Be(9);
    }

    [Fact]
    public async Task OutsideATenantPassTheFactoryIsNotAsked()
    {
        var tenantScopes = new AsyncOnlyTenantScopeFactory(Scopes);
        var store = new EFCoreJobStore(Scopes, new JobRunMapper(), tenantScopes);

        var id = await store.EnqueueAsync(Run("report"));

        (await store.FindAsync(id)).Should().NotBeNull();
        tenantScopes.BoundFor.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutAFactoryATenantPassUsesAPlainScope()
    {
        var store = new EFCoreJobStore(Scopes, new JobRunMapper());

        using (new BackgroundTenant("a"))
        {
            var id = await store.EnqueueAsync(Run("report"));

            (await store.FindAsync(id)).Should().NotBeNull();
        }
    }

    private IServiceScopeFactory Scopes => _provider.GetRequiredService<IServiceScopeFactory>();

    private static JobRun Run(string name) => new() { JobName = name, DueAt = Now };
}
