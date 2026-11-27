using System.Data;
using Bsft.Core.Persistence;
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
/// Parity of the shared transaction with the transaction seam a consuming application replaces: a conflict a nested unit raised
/// past EF Core and an outer handler caught is the cause of the refused commit and retries the whole command once,
/// and a cancellation after the work completed no longer stops the commit.
/// </summary>
/// <remarks>
/// Every fact is reported as skipped without a server; set <c>BSFT_TEST_POSTGRES</c> to run them.
/// </remarks>
public sealed class PostgresTransactionParityTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    [PostgresFact]
    public async Task SendAsync_WhenANestedCommandsRawSqlDeadlockIsCaught_RetriesTheOutermostCommandOnceInAFreshScope()
    {
        var connectionString = await fixture.CreateDatabaseAsync("parity_deadlock_catch");
        var log = new ParityTestLog { ConnectionString = connectionString };
        await using var provider = BuildProvider(connectionString, log);
        await using var scope = provider.CreateAsyncScope();

        var attempts = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .SendAsync(new NestedDeadlockCatchCommand("test"), CancellationToken.None);

        attempts.Should().Be(2, "the first attempt's nested deadlock and the swallowed exception trigger a retry");
        log.OuterHandlerRuns.Should().Be(2, "the outer handler runs exactly 2 times");
        log.NestedHandlerRuns.Should().Be(2, "the nested handler runs exactly 2 times");
        log.OuterContextInstances.Should().HaveCount(2, "there should be 2 different CatalogContext instances");
        log.OuterContextInstances[0].Should().NotBe(log.OuterContextInstances[1], "the second outer run got a fresh scope with a different context");
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(1L, "the catalog item is committed exactly once");
    }

    [PostgresFact]
    public async Task ExecuteAsync_WhenANestedUnitsFailureIsCaught_ReportsItAsInnerException()
    {
        var connectionString = await fixture.CreateDatabaseAsync("parity_nested_failure");
        var log = new ParityTestLog { ConnectionString = connectionString };
        await using var provider = BuildProvider(connectionString, log);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<IAmbientTransaction>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        PostgresException? swallowed = null;

        var exception = await Assert.ThrowsAsync<AmbientTransactionMisuseException>(async () =>
        {
            await unit.ExecuteAsync(async ct =>
            {
                catalog.Items.Add(new CatalogItem { Name = "item1" });
                await catalog.SaveChangesAsync(ct);

                try
                {
                    await unit.ExecuteAsync(async innerCt =>
                    {
                        var inventory = scope.ServiceProvider.GetRequiredService<InventoryContext>();
                        await RawSqlOnSharedConnection.RaiseAsync(inventory, "40P01", innerCt);
                        return 0;
                    }, ct);
                }
                catch (PostgresException caught)
                {
                    // The outer work swallows the nested failure and completes.
                    swallowed = caught;
                }

                return 0;
            }, CancellationToken.None);
        });

        // No EF Core command failed, so the nested unit's own failure is the only cause there is.
        exception.InnerException.Should().BeSameAs(swallowed);
        swallowed!.SqlState.Should().Be("40P01");
        unit.IsTransactionConflict(exception).Should().BeTrue();
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(0L);
    }

    [PostgresFact]
    public async Task SendAsync_WhenANestedCommandsCaughtFailureIsNoConflict_DoesNotRetry()
    {
        var connectionString = await fixture.CreateDatabaseAsync("parity_no_conflict");
        var log = new ParityTestLog { ConnectionString = connectionString };
        await using var provider = BuildProvider(connectionString, log);
        await using var scope = provider.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<AmbientTransactionMisuseException>(async () =>
        {
            await scope.ServiceProvider.GetRequiredService<IMediator>()
                .SendAsync(new NestedNonConflictFailureCommand("test"), CancellationToken.None);
        });

        exception.InnerException.Should().BeOfType<PostgresException>();
        var postgresEx = (PostgresException)exception.InnerException!;
        postgresEx.SqlState.Should().Be("P0001");
        log.OuterHandlerRuns.Should().Be(1, "non-conflict failures are not retried");
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(0L);
    }

    [PostgresFact]
    public async Task ExecuteAsync_WhenTheCallerCancelsAfterTheWorkCompleted_StillCommits()
    {
        var connectionString = await fixture.CreateDatabaseAsync("parity_cancel_after");
        var log = new ParityTestLog { ConnectionString = connectionString };
        await using var provider = BuildProvider(connectionString, log);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        using var cts = new CancellationTokenSource();

        await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "item_no_save" });
            // Note: NOT calling SaveChangesAsync, so the outermost unit saves it
            cts.Cancel();
            return 0;
        }, cts.Token);

        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should()
            .Be(1L, "cancelling after work still commits because SaveChanges and commit use CancellationToken.None");
    }

    [PostgresFact]
    public async Task ExecuteAsync_WhenTheCallerCancelsAfterTheWorkCompleted_WithExplicitSave_StillCommits()
    {
        var connectionString = await fixture.CreateDatabaseAsync("parity_cancel_after_save");
        var log = new ParityTestLog { ConnectionString = connectionString };
        await using var provider = BuildProvider(connectionString, log);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        using var cts = new CancellationTokenSource();

        await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "item_explicit_save" });
            await catalog.SaveChangesAsync(ct);
            cts.Cancel();
            return 0;
        }, cts.Token);

        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should()
            .Be(1L, "even with explicit SaveChanges before cancel, the outermost commit happens with CancellationToken.None");
    }

    [PostgresFact]
    public async Task ExecuteAsync_WhenTheCallerCancelsDuringTheWork_CommitsNothing()
    {
        var connectionString = await fixture.CreateDatabaseAsync("parity_cancel_during");
        var log = new ParityTestLog { ConnectionString = connectionString };
        await using var provider = BuildProvider(connectionString, log);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        using var cts = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await unit.ExecuteAsync(async ct =>
            {
                catalog.Items.Add(new CatalogItem { Name = "item1" });
                await catalog.SaveChangesAsync(ct);
                cts.Cancel();

                // Second operation observes the cancelled token
                await catalog.Database.ExecuteSqlRawAsync("SELECT 1", ct);
                return 0;
            }, cts.Token);
        });

        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should()
            .Be(0L, "cancelling during work rolls back everything");
    }

    [PostgresFact]
    public async Task SendAsync_WhenTheHandlerThrowsItsOwnExceptionAfterCatchingANestedConflict_DoesNotRetry()
    {
        var connectionString = await fixture.CreateDatabaseAsync("parity_catch_own_failure");
        var log = new ParityTestLog { ConnectionString = connectionString };
        await using var provider = BuildProvider(connectionString, log);
        await using var scope = provider.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await scope.ServiceProvider.GetRequiredService<IMediator>()
                .SendAsync(new NestedDeadlockCatchOwnFailureCommand("test"), CancellationToken.None);
        });

        exception.Message.Should().Be("own failure", "the outer handler's own exception is what escapes");
        log.OuterHandlerRuns.Should().Be(1, "the handler ran exactly once, not retried");
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should()
            .Be(0L, "nothing committed because the exception is not a conflict");
    }

    // A predicate that retries nothing on its own, so a retry here is the PostgreSQL classification.
    private static ServiceProvider BuildProvider(string connectionString, ParityTestLog log) =>
        BuildProvider(connectionString, log, _ => false);

    private static ServiceProvider BuildProvider(string connectionString, ParityTestLog log, Func<Exception?, bool> shouldRetry)
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
            options.ShouldRetry = shouldRetry;
        });
        services.AddBsftDataPostgresTransaction(connectionString);
        // Read Committed, the isolation level a consuming application runs the shared transaction with.
        services.Configure<BsftDataOptions>(options => options.SharedTransaction.IsolationLevel = IsolationLevel.ReadCommitted);
        services.AddDbContext<CatalogContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
        services.AddDbContext<InventoryContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory")));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
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
