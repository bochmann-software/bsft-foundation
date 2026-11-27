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
/// The shared transaction on a real PostgreSQL server: two module contexts in two schemas of one
/// database commit and roll back together, an advisory transaction lock is held until the outer commit,
/// and a real serialization failure in a nested command re-runs the whole outermost unit.
/// </summary>
/// <remarks>
/// Every fact is reported as skipped without a server; set <c>BSFT_TEST_POSTGRES</c> to run them.
/// </remarks>
public sealed class PostgresSharedTransactionTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private const long AdvisoryLockKey = 4711;

    [PostgresFact]
    public async Task ExecuteAsync_WhenBothSchemasAreWritten_CommitsThemTogether()
    {
        var connectionString = await fixture.CreateDatabaseAsync("commit");
        await using var provider = BuildProvider(connectionString, new ScenarioLog());
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);
            var inventory = scope.ServiceProvider.GetRequiredService<InventoryContext>();
            inventory.Stock.Add(new StockLevel { Id = 2, Sku = "B-2", Quantity = 5 });
            await inventory.SaveChangesAsync(ct);

            (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(0L);

            return 0;
        });

        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(1L);
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM inventory.stock")).Should().Be(2L);
    }

    [PostgresFact]
    public async Task ExecuteAsync_WhenAContextQueriesAfterTheUnit_ClosesTheConnectionItOpensItself()
    {
        var connectionString = await fixture.CreateDatabaseAsync("release");
        await using var provider = BuildProvider(connectionString, new ScenarioLog());
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);
            return 0;
        });

        (await catalog.Items.CountAsync()).Should().Be(1);

        catalog.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed,
            "the connection returns to the pool between units instead of staying pinned to the scope");
    }

    [PostgresFact]
    public async Task ExecuteAsync_WhenANestedContextAsksForItsTransactionFirst_FindsTheSharedOne()
    {
        var connectionString = await fixture.CreateDatabaseAsync("initialized");
        await using var provider = BuildProvider(connectionString, new ScenarioLog());
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);

            var inventory = scope.ServiceProvider.GetRequiredService<InventoryContext>();
            var callerTransaction = inventory.Database.CurrentTransaction;
            callerTransaction.Should().NotBeNull("a store joins this transaction rather than beginning its own");
            await callerTransaction!.CreateSavepointAsync("store_write", ct);
            inventory.Stock.Add(new StockLevel { Id = 2, Sku = "B-2", Quantity = 5 });
            await inventory.SaveChangesAsync(ct);
            await callerTransaction.ReleaseSavepointAsync("store_write", ct);

            return 0;
        });

        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(1L);
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM inventory.stock")).Should().Be(2L);
    }

    [PostgresFact]
    public async Task ExecuteAsync_WhenTheSecondSchemaFails_RollsBackTheFirst()
    {
        var connectionString = await fixture.CreateDatabaseAsync("rollback");
        await using var provider = BuildProvider(connectionString, new ScenarioLog());
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryContext>();

        var act = () => unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);
            inventory.Stock.Add(new StockLevel { Id = 2, Sku = "B-1", Quantity = 5 });
            await inventory.SaveChangesAsync(ct);

            return 0;
        });

        await act.Should().ThrowAsync<DbUpdateException>();
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(0L);
    }

    [PostgresFact]
    public async Task ExecuteAsync_WithAnAdvisoryTransactionLock_HoldsItUntilTheOuterCommit()
    {
        var connectionString = await fixture.CreateDatabaseAsync("advisory");
        await using var provider = BuildProvider(connectionString, new ScenarioLog());
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        await unit.ExecuteAsync(async ct =>
        {
            await catalog.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", ct);

            await unit.ExecuteAsync(async innerToken =>
            {
                var inventory = scope.ServiceProvider.GetRequiredService<InventoryContext>();
                (await inventory.Stock.CountAsync(innerToken)).Should().Be(1);
                return 0;
            }, ct);

            (await TryAdvisoryLockAsync(connectionString)).Should().BeFalse("the nested unit committed nothing, so the lock is still held");

            return 0;
        });

        (await TryAdvisoryLockAsync(connectionString)).Should().BeTrue("the outer commit released it");
    }

    [PostgresFact]
    public async Task SendAsync_WhenTheNestedCommandHitsASerializationFailure_RetriesTheWholeOutermostUnit()
    {
        var connectionString = await fixture.CreateDatabaseAsync("serialization");
        var log = new ScenarioLog { ConnectionString = connectionString, RacedAdjustments = 1 };
        await using var provider = BuildProvider(connectionString, log);
        await using var scope = provider.CreateAsyncScope();

        var attempts = await scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new PlaceOrderCommand("bolt"));

        attempts.Should().Be(2);
        log.AdjustmentAttempts.Should().Be(2, "the nested command is re-run with its outer attempt, never on its own");
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(1L);
        (await ScalarAsync(connectionString, "SELECT quantity FROM inventory.stock WHERE id = 1"))
            .Should().Be(2, "the concurrent writer's increment and the retried one both landed");
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_WhenTheNestedCommandSwallowsADeadlock_RetriesTheWholeOutermostUnit(bool isWritingAfterwards)
    {
        var connectionString = await fixture.CreateDatabaseAsync("swallowed");
        var log = new ScenarioLog
        {
            ConnectionString = connectionString,
            SwallowedDeadlocks = 1,
            IsWritingAfterTheSwallowedDeadlock = isWritingAfterwards,
        };
        await using var provider = BuildProvider(connectionString, log);
        await using var scope = provider.CreateAsyncScope();

        var attempts = await scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new PlaceOrderCommand("bolt"));

        attempts.Should().Be(2, "the first attempt's commit was refused and the swallowed 40P01 classified it as a conflict");
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(1L);
        (await ScalarAsync(connectionString, "SELECT quantity FROM inventory.stock WHERE id = 1")).Should().Be(1);
    }

    [PostgresFact]
    public async Task SendAsync_WhenTheHandlerSwallowsANestedFailureThatIsNoConflict_RunsItOnceUnderTheDefaultPredicate()
    {
        var connectionString = await fixture.CreateDatabaseAsync("misused");
        var log = new ScenarioLog { ConnectionString = connectionString, FailedAdjustments = 1, IsSwallowingTheAdjustmentFailure = true };
        await using var provider = BuildProvider(connectionString, log, BsftResilienceMediatorOptions.IsTransientFailure);
        await using var scope = provider.CreateAsyncScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new PlaceOrderCommand("bolt"));

        (await act.Should().ThrowAsync<AmbientTransactionMisuseException>()).WithInnerException<PostgresException>();
        log.OrderAttempts.Should().Be(1, "swallowing the failure fails the same way on every attempt, so it is not retried");
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(0L);
    }

    [PostgresFact]
    public async Task SendAsync_WhenAnotherFlowOfTheScopeRunsAUnit_RefusesTheCommandInsteadOfRetryingItInAFreshScope()
    {
        var connectionString = await fixture.CreateDatabaseAsync("overlapping");
        var log = new ScenarioLog { ConnectionString = connectionString };
        await using var provider = BuildProvider(connectionString, log, BsftResilienceMediatorOptions.IsTransientFailure);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<IAmbientTransaction>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = unit.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await release.Task;

            return 0;
        });
        await started.Task;

        var act = () => scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new PlaceOrderCommand("bolt"));

        (await act.Should().ThrowAsync<AmbientTransactionMisuseException>()).WithMessage("*one unit of work at a time*");
        release.SetResult();
        await running;
        log.OrderAttempts.Should().Be(0, "a retry in a fresh scope would have hidden the overlap by succeeding");
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(0L);
    }

    [PostgresFact]
    public async Task ExecuteAsync_WhenAFailedQueryIsSwallowed_FailsInsteadOfReportingACommitThatDidNotHappen()
    {
        var connectionString = await fixture.CreateDatabaseAsync("doomed");
        await using var provider = BuildProvider(connectionString, new ScenarioLog { ConnectionString = connectionString });
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var act = () => unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);

            try
            {
                await catalog.Database.ExecuteSqlRawAsync("SELECT * FROM catalog.no_such_table", ct);
            }
            catch (PostgresException)
            {
                // swallowed
            }

            return 0;
        });

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithInnerException<PostgresException>();
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(0L);
    }

    [PostgresFact]
    public async Task ExecuteAsync_WhenARowFailsWhileTheHandlerReadsAndItSwallowsTheFailure_FailsTheUnitAndPersistsNothing()
    {
        var connectionString = await fixture.CreateDatabaseAsync("readfailure");
        await using var provider = BuildProvider(connectionString, new ScenarioLog { ConnectionString = connectionString });
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var act = () => unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);

            try
            {
                // The query starts fine and streams rows; the division by zero comes with row 1000, while
                // EF Core reads, where no command interceptor sees it.
                await catalog.Database
                    .SqlQueryRaw<int>("SELECT 1 / (x - 1000) AS \"Value\" FROM generate_series(1, 2000) AS x")
                    .ToListAsync(ct);
            }
            catch (PostgresException)
            {
                // swallowed
            }

            return 0;
        });

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.InFailedSqlTransaction, "the probe asked the server before the commit");
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(0L);
    }

    [PostgresFact]
    public async Task ExecuteAsync_WhenAUniqueViolationFromSaveChangesIsCaught_CommitsTheOtherWrites()
    {
        var connectionString = await fixture.CreateDatabaseAsync("savepoint");
        await using var provider = BuildProvider(connectionString, new ScenarioLog { ConnectionString = connectionString });
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryContext>();

        await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);

            var duplicate = inventory.Stock.Add(new StockLevel { Id = 2, Sku = "B-1", Quantity = 5 });
            var save = () => inventory.SaveChangesAsync(ct);
            (await save.Should().ThrowAsync<DbUpdateException>())
                .WithInnerException<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);

            // The handler answers the duplicate differently and goes on.
            duplicate.State = EntityState.Detached;
            inventory.Stock.Add(new StockLevel { Id = 3, Sku = "B-3", Quantity = 1 });
            await inventory.SaveChangesAsync(ct);

            return 0;
        });

        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM catalog.items")).Should().Be(1L);
        (await ScalarAsync(connectionString, "SELECT COUNT(*) FROM inventory.stock")).Should().Be(2L,
            "EF Core rolled the failed save back to its savepoint, so the transaction committed the rest");
    }

    // A predicate that retries nothing on its own, so a retry here is the PostgreSQL classification.
    private static ServiceProvider BuildProvider(string connectionString, ScenarioLog log) =>
        BuildProvider(connectionString, log, _ => false);

    private static ServiceProvider BuildProvider(string connectionString, ScenarioLog log, Func<Exception?, bool> shouldRetry)
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
        services.Configure<BsftDataOptions>(options => options.SharedTransaction.IsolationLevel = IsolationLevel.Serializable);
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

    private static async Task<bool> TryAdvisoryLockAsync(string connectionString) =>
        (bool)(await ScalarAsync(connectionString, $"SELECT pg_try_advisory_xact_lock({AdvisoryLockKey})"))!;
}
