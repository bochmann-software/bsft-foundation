using Bsft.Data.MultiTenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Outbox.EFCore.Tests.Support;

/// <summary>
/// A fixture for testing tenant-aware outbox stores with a test ITenantContext that can be switched
/// between tenants.
/// </summary>
internal sealed class TenantAwareFixture : IDisposable
{
    /// <summary>The connection the in-memory database lives in for as long as it is open.</summary>
    private readonly SqliteConnection _connection;

    /// <summary>The container the context is resolved from, one scope per read or write.</summary>
    private readonly ServiceProvider _provider;

    /// <summary>The test tenant context that tracks the current tenant.</summary>
    private readonly TestTenantContext _tenantContext;

    /// <summary>Initializes a new instance of the <see cref="TenantAwareFixture"/> class.</summary>
    internal TenantAwareFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext = new TestTenantContext();

        var services = new ServiceCollection();
        services.AddSingleton(_tenantContext); // Register as singleton so SetCurrentTenant works across scopes
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TestTenantContext>());
        services.AddDbContext<TenantAwareDbContext>(options => options.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantAwareDbContext>().Database.EnsureCreated();
    }

    /// <summary>Gets the scope factory the store under test resolves its context from.</summary>
    internal IServiceScopeFactory Scopes => _provider.GetRequiredService<IServiceScopeFactory>();

    /// <summary>Sets the current tenant for subsequent operations.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    internal void SetCurrentTenant(string? tenantId) => _tenantContext.TenantId = tenantId;

    /// <summary>Commits one tenant-aware provisioning intent.</summary>
    /// <param name="tenantId">The tenant the intent is for.</param>
    /// <returns>The identity the row was written under.</returns>
    internal async Task<long> GivenIntentAsync(string tenantId)
    {
        var row = new TenantAwareIntentRow
        {
            TenantId = tenantId,
            IntentName = tenantId,
        };

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantAwareDbContext>();

        db.Intents.Add(row);
        await db.SaveChangesAsync();

        return row.Id;
    }

    /// <summary>Reads a row back as it stands in the database.</summary>
    /// <param name="id">The identity to read.</param>
    /// <returns>The row.</returns>
    internal async Task<TenantAwareIntentRow> ReadRowAsync(long id)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantAwareDbContext>();

        return await db.Intents.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}
