using System.Data;
using Bsft.Core.Persistence;
using Bsft.Data.DependencyInjection;
using Bsft.Data.MultiTenancy;
using Bsft.Data.Postgres.DependencyInjection;
using Bsft.Data.Postgres.Tests.Support;
using Bsft.Mediator;
using Bsft.Mediator.DependencyInjection;
using Bsft.Resilience.Mediator;
using Bsft.Resilience.Mediator.DependencyInjection;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Bsft.Data.Postgres.Tests;

/// <summary>
/// ADR item 9: <see cref="Bsft.Data.Postgres.DependencyInjection.DataPostgresServiceCollectionExtensions.AddBsftDataPostgresTransaction(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Func{System.IServiceProvider,Npgsql.NpgsqlConnection})"/>
/// binds the tenant database per scope when the connection factory reads <see cref="ITenantContext"/>.
/// </summary>
/// <remarks>
/// Every fact is reported as skipped without a server; set <c>BSFT_TEST_POSTGRES</c> to run them.
/// </remarks>
public sealed class PostgresTenantConnectionTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private const string TenantA = "tenant_a";
    private const string TenantB = "tenant_b";

    [PostgresFact]
    public async Task ExecuteAsync_WithTenantA_WritesToTenantADatabase()
    {
        var (connStringA, connStringB) = await CreateTenantDatabasesAsync();

        try
        {
            await using var provider = BuildProvider(connStringA, connStringB);
            await using var scope = provider.CreateAsyncScope();

            // Set tenant A before resolving the transaction
            scope.ServiceProvider.EnterTenantScope(TenantA, _ => "tenant not set");

            var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

            await unit.ExecuteAsync(async ct =>
            {
                catalog.Items.Add(new CatalogItem { Name = "item_a" });
                await catalog.SaveChangesAsync(ct);
                return 0;
            });

            // Verify the write went to tenant A, not tenant B
            (await ScalarAsync(connStringA, "SELECT COUNT(*) FROM catalog.items")).Should().Be(1L,
                "tenant A should have the written item");
            (await ScalarAsync(connStringB, "SELECT COUNT(*) FROM catalog.items")).Should().Be(0L,
                "tenant B should be empty");
        }
        finally
        {
            await DropTenantDatabasesAsync();
        }
    }

    [PostgresFact]
    public async Task ExecuteAsync_WithTenantB_WritesToTenantBDatabase()
    {
        var (connStringA, connStringB) = await CreateTenantDatabasesAsync();

        try
        {
            await using var provider = BuildProvider(connStringA, connStringB);
            await using var scope = provider.CreateAsyncScope();

            // Set tenant B before resolving the transaction
            scope.ServiceProvider.EnterTenantScope(TenantB, _ => "tenant not set");

            var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

            await unit.ExecuteAsync(async ct =>
            {
                catalog.Items.Add(new CatalogItem { Name = "item_b" });
                await catalog.SaveChangesAsync(ct);
                return 0;
            });

            // Verify the write went to tenant B, not tenant A
            (await ScalarAsync(connStringA, "SELECT COUNT(*) FROM catalog.items")).Should().Be(0L,
                "tenant A should be empty");
            (await ScalarAsync(connStringB, "SELECT COUNT(*) FROM catalog.items")).Should().Be(1L,
                "tenant B should have the written item");
        }
        finally
        {
            await DropTenantDatabasesAsync();
        }
    }

    [PostgresFact]
    public async Task SendAsync_WhenARetryOccurs_StaysInTheSameTenantDatabase()
    {
        var (connStringA, connStringB) = await CreateTenantDatabasesAsync();

        try
        {
            var attempts = new TenantRetryLog();
            await using var provider = BuildProvider(connStringA, connStringB, attempts);
            await using var scope = provider.CreateAsyncScope();

            // Set tenant A before resolving the transaction
            scope.ServiceProvider.EnterTenantScope(TenantA, _ => "tenant not set");

            var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
                .SendAsync(new WriteTenantItemCommand("item_a"));

            // Verify the write went to tenant A even after the retry
            (await ScalarAsync(connStringA, "SELECT COUNT(*) FROM catalog.items")).Should().Be(1L,
                "tenant A should have the written item after retry");
            (await ScalarAsync(connStringB, "SELECT COUNT(*) FROM catalog.items")).Should().Be(0L,
                "tenant B should still be empty");
            attempts.CommandAttempts.Should().Be(2, "the command should have been retried once");
        }
        finally
        {
            await DropTenantDatabasesAsync();
        }
    }

    /// <summary>
    /// Counterproof: the scope seed is required to carry tenant context across retry scopes. Without it,
    /// when a transient error triggers a retry in a fresh scope, the tenantContext.TenantId is not set
    /// (the tenant context only existed in the original scope). The connection factory receives an unset TenantId,
    /// which fails the condition `tenantContext.TenantId == TenantA`, so it falls through to use connStringB.
    /// The result is data persistence to the wrong tenant database: what should go to tenant A ends up in tenant B.
    /// The scope seed is essential to prevent this cross-contamination of tenant data.
    /// </summary>
    [PostgresFact]
    public async Task SendAsync_WhenARetryRunsWithoutTheScopeSeed_DoesNotReachTheTenantDatabase()
    {
        var (connStringA, connStringB) = await CreateTenantDatabasesAsync();

        try
        {
            var attempts = new TenantRetryLog();
            await using var provider = BuildProviderWithoutScopeSeed(connStringA, connStringB, attempts);
            await using var scope = provider.CreateAsyncScope();

            // Set tenant A before resolving the transaction
            scope.ServiceProvider.EnterTenantScope(TenantA, _ => "tenant not set");

            // Send a command that forces a transient error on first attempt, triggering a retry.
            // The first attempt runs with tenant A set and triggers a serialization failure.
            // The retry runs in a fresh scope where tenant A is not set, so the connection factory
            // falls through to use the default connection string (connStringB).
            await scope.ServiceProvider.GetRequiredService<IMediator>()
                .SendAsync(new WriteTenantItemCommand("item_a"));

            // Without the scope seed, the data ends up in the wrong tenant database.
            (await ScalarAsync(connStringA, "SELECT COUNT(*) FROM catalog.items")).Should().Be(0L,
                "tenant A does not reach the database because only the first attempt ran there");
            (await ScalarAsync(connStringB, "SELECT COUNT(*) FROM catalog.items")).Should().Be(1L,
                "the retry in the fresh scope (without tenant context) defaults to tenant B's connection");
            attempts.CommandAttempts.Should().Be(2, "the command retried once");
        }
        finally
        {
            await DropTenantDatabasesAsync();
        }
    }

    private static ServiceProvider BuildProvider(string connStringA, string connStringB, TenantRetryLog? log = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (log is not null)
            services.AddSingleton(log);

        services.AddBsftMediator(mediator => mediator
            .RegisterServicesFromAssembly(typeof(PlaceOrderCommandHandler).Assembly)
            .UseDefaults());
        services.AddBsftResilienceMediator(options =>
        {
            options.BaseDelay = TimeSpan.Zero;
        });

        // Register the connection factory that selects by tenant
        services.AddBsftDataPostgresTransaction(sp =>
        {
            var tenantContext = sp.GetRequiredService<ITenantContext>();
            var connectionString = tenantContext.TenantId == TenantA ? connStringA : connStringB;
            return new NpgsqlConnection(connectionString);
        });

        services.AddBsftDataMultiTenancy(_ => { });
        services.AddScoped<TenantRetryLogScopeSeed>();
        services.AddScoped<IRequestScopeSeed>(sp => sp.GetRequiredService<TenantRetryLogScopeSeed>());

        services.Configure<BsftDataOptions>(options => options.SharedTransaction.IsolationLevel = IsolationLevel.Serializable);
        services.AddDbContext<CatalogContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
        services.AddDbContext<InventoryContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory")));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static ServiceProvider BuildProviderWithoutScopeSeed(string connStringA, string connStringB, TenantRetryLog log)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(log);
        services.AddBsftMediator(mediator => mediator
            .RegisterServicesFromAssembly(typeof(PlaceOrderCommandHandler).Assembly)
            .UseDefaults());
        services.AddBsftResilienceMediator(options =>
        {
            options.BaseDelay = TimeSpan.Zero;
        });

        // Register the connection factory that selects by tenant
        services.AddBsftDataPostgresTransaction(sp =>
        {
            var tenantContext = sp.GetRequiredService<ITenantContext>();
            var connectionString = tenantContext.TenantId == TenantA ? connStringA : connStringB;
            return new NpgsqlConnection(connectionString);
        });

        services.AddBsftDataMultiTenancy(_ => { });
        // NOTE: TenantRetryLogScopeSeed is NOT registered here, so the tenant context is not carried to the fresh scope

        services.Configure<BsftDataOptions>(options => options.SharedTransaction.IsolationLevel = IsolationLevel.Serializable);
        services.AddDbContext<CatalogContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
        services.AddDbContext<InventoryContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory")));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private async Task<(string, string)> CreateTenantDatabasesAsync()
    {
        var connStringA = await fixture.CreateDatabaseAsync(TenantA);
        var connStringB = await fixture.CreateDatabaseAsync(TenantB);
        return (connStringA, connStringB);
    }

    private async Task DropTenantDatabasesAsync()
    {
        // Disposal of the fixture handles cleanup
        await Task.CompletedTask;
    }

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
