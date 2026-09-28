using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Outbox.EFCore.Tests.Support;

/// <summary>One in-memory SQLite database holding the provisioning outbox, and the two reads and
/// writes every suite over it needs.</summary>
/// <remarks>
/// Both suites in this project run against the same table through the same context. Spelling the
/// connection, the registration and the schema creation out in each of them means a change to any of
/// the three has to be made twice, and a suite that is missed keeps passing against a schema nothing
/// else has any more.
/// </remarks>
internal sealed class ProvisioningFixture : IDisposable
{
    /// <summary>The connection the in-memory database lives in for as long as it is open.</summary>
    private readonly SqliteConnection _connection;

    /// <summary>The container the context is resolved from, one scope per read or write.</summary>
    private readonly ServiceProvider _provider;

    /// <summary>Initializes a new instance of the <see cref="ProvisioningFixture"/> class.</summary>
    internal ProvisioningFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<ProvisioningDbContext>(options => options.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ProvisioningDbContext>().Database.EnsureCreated();
    }

    /// <summary>Gets the scope factory the store under test resolves its context from.</summary>
    internal IServiceScopeFactory Scopes => _provider.GetRequiredService<IServiceScopeFactory>();

    /// <summary>Commits one provisioning intent, the way a transaction alongside it would.</summary>
    /// <param name="tenantId">The tenant the intent is for.</param>
    /// <returns>The identity the row was written under.</returns>
    internal async Task<long> GivenIntentAsync(string tenantId)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProvisioningDbContext>();
        var row = new ProvisioningIntentRow { TenantId = tenantId };

        db.Intents.Add(row);
        await db.SaveChangesAsync();

        return row.Id;
    }

    /// <summary>Reads a row back as it stands in the database.</summary>
    /// <param name="id">The identity to read.</param>
    /// <returns>The row.</returns>
    internal async Task<ProvisioningIntentRow> ReadRowAsync(long id)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProvisioningDbContext>();

        return await db.Intents.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}
