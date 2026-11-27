using System.Data;
using System.Data.Common;
using Bsft.Core.Persistence;
using Bsft.Data.Tests.Fakes;
using Bsft.Data.DependencyInjection;
using Bsft.Data.MultiTenancy;
using Bsft.Data.Tests.Support;
using Bsft.Data.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Tests.Transactions;

/// <summary>
/// One transaction across two module contexts on one shared connection: what commits together, what
/// rolls back together, how a nested unit joins, and how a context resolved late enlists.
/// </summary>
public sealed class SharedTransactionTests : IDisposable
{
    private readonly SharedTransactionDatabase _database = new();

    [Fact]
    public async Task ExecuteAsync_WhenBothContextsSave_CommitsBothOnce()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var stock = scope.ServiceProvider.GetRequiredService<StockContext>();

        var answer = await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);
            stock.Items.Add(new StockItem { Sku = "B-1" });
            await stock.SaveChangesAsync(ct);

            _database.Count("CatalogItems").Should().Be(0, "nothing is visible before the outermost unit commits");

            return "done";
        });

        answer.Should().Be("done");
        _database.Count("CatalogItems").Should().Be(1);
        _database.Count("StockItems").Should().Be(1);
        catalog.Database.CurrentTransaction.Should().BeNull("the ended transaction is taken back from every context");
        stock.Database.CurrentTransaction.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheSecondContextFails_RollsBackTheFirst()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var stock = scope.ServiceProvider.GetRequiredService<StockContext>();

        var act = () => unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);
            stock.Items.Add(new StockItem { Sku = "DUP" });
            stock.Items.Add(new StockItem { Sku = "DUP" });
            await stock.SaveChangesAsync(ct);

            return 0;
        });

        await act.Should().ThrowAsync<DbUpdateException>();
        _database.Count("CatalogItems").Should().Be(0);
        _database.Count("StockItems").Should().Be(0);
        catalog.ChangeTracker.Entries().Should().BeEmpty("a rolled-back unit leaves no tracked rows behind");
        stock.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenNested_JoinsAndCommitsOnlyAtTheOutermostUnit()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<IAmbientTransaction>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var stock = scope.ServiceProvider.GetRequiredService<StockContext>();

        unit.IsActive.Should().BeFalse();

        await unit.ExecuteAsync(async ct =>
        {
            unit.IsActive.Should().BeTrue();
            catalog.Items.Add(new CatalogItem { Name = "outer" });
            await catalog.SaveChangesAsync(ct);

            await unit.ExecuteAsync(async innerToken =>
            {
                stock.Items.Add(new StockItem { Sku = "inner" });
                await stock.SaveChangesAsync(innerToken);

                return 0;
            }, ct);

            _database.Count("StockItems").Should().Be(0, "a nested unit commits nothing on its own");

            return 0;
        });

        unit.IsActive.Should().BeFalse();
        _database.Count("CatalogItems").Should().Be(1);
        _database.Count("StockItems").Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenANestedFailureIsSwallowed_RefusesToCommit()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var act = () => unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "outer" });
            await catalog.SaveChangesAsync(ct);

            var nested = () => unit.ExecuteAsync<int>(_ => throw new InvalidOperationException("nested failure"), ct);
            await nested.Should().ThrowAsync<InvalidOperationException>().WithMessage("nested failure");

            return 0;
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("A unit nested in the shared transaction failed*");
        _database.Count("CatalogItems").Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAFailedQueryIsSwallowed_FailsTheUnitAndPersistsNothing()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var act = () => unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);

            try
            {
                await catalog.Database.ExecuteSqlRawAsync("SELECT * FROM NoSuchTable", ct);
            }
            catch (SqliteException)
            {
                // The handler swallows the failure; the unit must not report success anyway.
            }

            return 0;
        });

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithInnerException<SqliteException>();
        _database.Count("CatalogItems").Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenASynchronousFailureIsSwallowed_FailsTheUnit()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var act = () => unit.ExecuteAsync(_ =>
        {
            try
            {
                catalog.Database.ExecuteSqlRaw("SELECT * FROM NoSuchTable");
            }
            catch (SqliteException)
            {
                // swallowed
            }

            return Task.FromResult(0);
        });

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithInnerException<SqliteException>();
    }

    [Fact]
    public async Task ExecuteAsync_WhenACancelledCommandIsSwallowed_FailsTheUnit()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => unit.ExecuteAsync(async ct =>
        {
            await catalog.Items.CountAsync(ct);

            try
            {
                await catalog.Database.ExecuteSqlRawAsync("SELECT 1", cancelled.Token);
            }
            catch (OperationCanceledException)
            {
                // swallowed
            }

            return 0;
        });

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithInnerException<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_WhenASwallowedSaveFailureWasRolledBackToItsSavepoint_CommitsTheRest()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var stock = scope.ServiceProvider.GetRequiredService<StockContext>();

        await unit.ExecuteAsync(async ct =>
        {
            stock.Items.Add(new StockItem { Sku = "B-1" });
            await stock.SaveChangesAsync(ct);

            var duplicate = stock.Items.Add(new StockItem { Sku = "B-1" });
            var asynchronousSave = () => stock.SaveChangesAsync(ct);
            await asynchronousSave.Should().ThrowAsync<DbUpdateException>();

            // The handler answers the unique violation differently and goes on.
            duplicate.State = EntityState.Detached;
            stock.Items.Add(new StockItem { Sku = "B-2" });
            stock.SaveChanges();

            var synchronousDuplicate = stock.Items.Add(new StockItem { Sku = "B-2" });
            FluentActions.Invoking(() => stock.SaveChanges()).Should().Throw<DbUpdateException>();
            synchronousDuplicate.State = EntityState.Detached;

            catalog.Items.Add(new CatalogItem { Name = "bolt" });

            return 0;
        });

        _database.Count("StockItems").Should().Be(2, "EF Core rolled the failed saves back to their savepoints");
        _database.Count("CatalogItems").Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenASwallowedSaveFailureHadNoSavepoint_FailsTheUnit()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var stock = scope.ServiceProvider.GetRequiredService<StockContext>();
        stock.Database.AutoSavepointsEnabled = false;

        var act = () => unit.ExecuteAsync(async ct =>
        {
            stock.Items.Add(new StockItem { Sku = "B-1" });
            stock.Items.Add(new StockItem { Sku = "B-1" });

            try
            {
                await stock.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // swallowed; without a savepoint nothing says the transaction is still healthy
            }

            return 0;
        });

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithInnerException<SqliteException>();
        _database.Count("StockItems").Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheProbeFindsTheTransactionAborted_FailsTheUnitAndPersistsNothing()
    {
        var probe = new FakeTransactionAbortProbe(new TimeoutException("aborted while rows were read"));
        await using var provider = _database.BuildProvider(services =>
            services.AddSingleton<ITransactionAbortProbe>(probe));
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var act = () => unit.ExecuteAsync(ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });

            return Task.FromResult(0);
        });

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithInnerException<TimeoutException>();
        probe.Probes.Should().Be(1);
        _database.Count("CatalogItems").Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheProbeFindsTheTransactionHealthy_Commits()
    {
        var probe = new FakeTransactionAbortProbe(abort: null);
        await using var provider = _database.BuildProvider(services =>
            services.AddSingleton<ITransactionAbortProbe>(probe));
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });

            return await unit.ExecuteAsync(_ => Task.FromResult(0), ct);
        });

        probe.Probes.Should().Be(1, "only the outermost unit asks, right before it commits");
        _database.Count("CatalogItems").Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnotherFlowOfTheScopeStartsAnOutermostUnit_RefusesIt()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<IAmbientTransaction>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var isActiveInside = false;

        var first = unit.ExecuteAsync(async _ =>
        {
            isActiveInside = unit.IsActive;
            started.SetResult();
            await release.Task;
            catalog.Items.Add(new CatalogItem { Name = "bolt" });

            return 0;
        });
        await started.Task;

        unit.IsActive.Should().BeFalse("the running unit belongs to another flow");
        var overlapping = () => unit.ExecuteAsync(_ => Task.FromResult(0));
        (await overlapping.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*one unit of work at a time*");

        release.SetResult();
        await first;

        isActiveInside.Should().BeTrue();
        _database.Count("CatalogItems").Should().Be(1, "the refused unit left the running one alone");
        await unit.ExecuteAsync(_ => Task.FromResult(0));
    }

    [Fact]
    public async Task ExecuteAsync_WhenAContextIsResolvedAfterTheTransactionBegan_EnlistsIt()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();

        var act = () => unit.ExecuteAsync<int>(async ct =>
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
            catalog.Items.Add(new CatalogItem { Name = "late" });
            await catalog.SaveChangesAsync(ct);

            var stock = scope.ServiceProvider.GetRequiredService<StockContext>();
            (await stock.Items.CountAsync(ct)).Should().Be(0, "a query of a late context runs in the transaction too");
            stock.Database.CurrentTransaction!.GetDbTransaction()
                .Should().BeSameAs(catalog.Database.CurrentTransaction!.GetDbTransaction());

            throw new InvalidOperationException("abandon");
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("abandon");
        _database.Count("CatalogItems").Should().Be(0, "the late context's write was part of the rolled-back unit");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAContextHoldsUnsavedChanges_SavesThemBeforeTheCommit()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        _ = scope.ServiceProvider.GetRequiredService<StockContext>();

        await unit.ExecuteAsync(_ =>
        {
            catalog.Items.Add(new CatalogItem { Name = "staged" });
            return Task.FromResult(0);
        });

        _database.Count("CatalogItems").Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheContextSavesSynchronously_EnlistsItAsWell()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();

        var act = () => unit.ExecuteAsync<int>(_ =>
        {
            var stock = scope.ServiceProvider.GetRequiredService<StockContext>();
            stock.Items.Add(new StockItem { Sku = "sync" });
            stock.SaveChanges();

            throw new InvalidOperationException("abandon");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        _database.Count("StockItems").Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithTheTenantInterceptorOnAnEnlistedContext_StampsAndRollsBackWithTheUnit()
    {
        await using var provider = _database.BuildProvider(services =>
        {
            services.AddScoped<TenantContext>();
            services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
            services.AddScoped<TenantSaveChangesInterceptor>();
            services.AddDbContext<TenantFilterDbContext>((sp, options) => options
                .UseBsftSharedTransaction(sp, (builder, connection) => builder.UseSqlite(connection))
                .AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>()));
        });
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenantId("tenant-1");
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var tenantScoped = scope.ServiceProvider.GetRequiredService<TenantFilterDbContext>();

        await unit.ExecuteAsync(async ct =>
        {
            tenantScoped.TestEntities.Add(new TestEntity { Id = 1, Name = "kept", Email = "a@b.c" });
            await tenantScoped.SaveChangesAsync(ct);
            return 0;
        });

        var abandoned = () => unit.ExecuteAsync<int>(async ct =>
        {
            tenantScoped.TestEntities.Add(new TestEntity { Id = 2, Name = "dropped", Email = "a@b.c" });
            await tenantScoped.SaveChangesAsync(ct);
            throw new InvalidOperationException("abandon");
        });

        await abandoned.Should().ThrowAsync<InvalidOperationException>();
        _database.Count("TestEntities").Should().Be(1);
        _database.Scalar("SELECT TenantId FROM TestEntities").Should().Be("tenant-1");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheConnectionWasClosed_ClosesItAgainAfterTheUnit()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();

        await unit.ExecuteAsync(_ => Task.FromResult(0));

        catalog.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheConnectionIsAlreadyOpen_LeavesItOpen()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        await catalog.Database.OpenConnectionAsync();

        await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "open" });
            await catalog.SaveChangesAsync(ct);
            return 0;
        });

        catalog.Database.GetDbConnection().State.Should().Be(ConnectionState.Open);
        _database.Count("CatalogItems").Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullWork_Throws()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();

        var act = () => unit.ExecuteAsync<int>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnEnlistedContextRetriesOnFailure_RefusesToEnlistIt()
    {
        await using var provider = _database.BuildProvider(
            sqliteOptions: sqlite => sqlite.ExecutionStrategy(dependencies => new RetryingStrategy(dependencies)));
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        _ = scope.ServiceProvider.GetRequiredService<StockContext>();

        var act = () => unit.ExecuteAsync(_ => Task.FromResult(0));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*EnableRetryOnFailure*");
    }

    [Fact]
    public async Task CommitAsync_OutsideAUnit_SavesEveryContextTogether()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var commit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var stock = scope.ServiceProvider.GetRequiredService<StockContext>();
        catalog.Items.Add(new CatalogItem { Name = "staged" });
        stock.Items.Add(new StockItem { Sku = "DUP" });
        stock.Items.Add(new StockItem { Sku = "DUP" });

        var act = () => commit.CommitAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
        _database.Count("CatalogItems").Should().Be(0, "the commit is one unit across both contexts");
    }

    [Fact]
    public async Task CommitAsync_WhenNothingIsStaged_Succeeds()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var commit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        _ = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        await commit.CommitAsync();

        _database.Count("CatalogItems").Should().Be(0);
    }

    [Fact]
    public async Task IsTransactionConflict_AsksEveryRegisteredClassifier()
    {
        await using var provider = _database.BuildProvider(services =>
        {
            services.AddSingleton<ITransactionConflictClassifier, FakeTransactionConflictClassifier<TimeoutException>>();
            services.AddSingleton<ITransactionConflictClassifier, FakeTransactionConflictClassifier<SqliteException>>();
        });
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<IAmbientTransaction>();

        unit.IsTransactionConflict(new SqliteException("busy", 5)).Should().BeTrue();
        unit.IsTransactionConflict(new InvalidOperationException()).Should().BeFalse();
    }

    [Fact]
    public async Task IsTransactionConflict_AfterAUnitWithASwallowedConflict_ClassifiesTheLaterFailureByIt()
    {
        await using var provider = _database.BuildProvider(services =>
            services.AddSingleton<ITransactionConflictClassifier, FakeTransactionConflictClassifier<SqliteException>>());
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<IAmbientTransaction>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var act = () => unit.ExecuteAsync<int>(async ct =>
        {
            try
            {
                await catalog.Database.ExecuteSqlRawAsync("SELECT * FROM NoSuchTable", ct);
            }
            catch (SqliteException)
            {
                // swallowed; what fails afterwards says nothing about the conflict
            }

            throw new InvalidOperationException("current transaction is aborted");
        });

        var failure = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        unit.IsTransactionConflict(failure).Should().BeTrue("the swallowed command failure is the conflict");

        await unit.ExecuteAsync(_ => Task.FromResult(0));
        unit.IsTransactionConflict(failure).Should().BeFalse("the next unit forgot the earlier failure");
    }

    [Fact]
    public async Task IsTransactionConflict_WithoutAClassifier_IsFalse()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<IAmbientTransaction>();

        unit.IsTransactionConflict(new TimeoutException()).Should().BeFalse();

        var act = () => unit.IsTransactionConflict(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Dispose_DisposesTheConnection()
    {
        var connection = new SqliteConnection(_database.ConnectionString);
        var transaction = NewTransaction(connection);
        connection.Open();

        transaction.Dispose();

        connection.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheConnection()
    {
        var connection = new SqliteConnection(_database.ConnectionString);
        var transaction = NewTransaction(connection);
        await connection.OpenAsync();

        await transaction.DisposeAsync();

        connection.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public void Constructor_WithANullArgument_Throws()
    {
        using var connection = new SqliteConnection(_database.ConnectionString);
        var options = Options.Create(new BsftDataOptions());
        var enlistment = new SharedTransactionEnlistment();
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var contexts = new SharedContextProvider(serviceProvider, enlistment);
        var health = new SharedTransactionHealth(enlistment, [], []);
        var steps = new SharedTransactionStepReporter([], NullLogger<SharedTransactionStepReporter>.Instance);
        var logger = NullLogger<SharedTransaction>.Instance;

        FluentActions.Invoking(() => new SharedTransaction(null!, contexts, options, health, steps, logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("connection");
        FluentActions.Invoking(() => new SharedTransaction(connection, null!, options, health, steps, logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("contexts");
        FluentActions.Invoking(() => new SharedTransaction(connection, contexts, null!, health, steps, logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        FluentActions.Invoking(() => new SharedTransaction(connection, contexts, options, null!, steps, logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("health");
        FluentActions.Invoking(() => new SharedTransaction(connection, contexts, options, health, null!, logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("steps");
        FluentActions.Invoking(() => new SharedTransaction(connection, contexts, options, health, steps, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAContextQueriesAfterTheUnit_ClosesTheConnectionItOpensItself()
    {
        await using var provider = _database.BuildProvider();
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
            "the unit gave back the hold on the connection it took, so the context closes what it opened");
    }

    [Fact]
    public async Task ExecuteAsync_WhenANestedCommandResolvesANewContext_ItsCurrentTransactionIsTheSharedOne()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "outer" });
            await catalog.SaveChangesAsync(ct);

            return await unit.ExecuteAsync(async innerToken =>
            {
                // What a module store does: join the caller's transaction, or begin one of its own.
                var stock = scope.ServiceProvider.GetRequiredService<StockContext>();
                var callerTransaction = stock.Database.CurrentTransaction;
                callerTransaction.Should().NotBeNull("the context took the shared transaction as it initialized");
                callerTransaction!.GetDbTransaction().Should().BeSameAs(catalog.Database.CurrentTransaction!.GetDbTransaction());

                stock.Items.Add(new StockItem { Sku = "inner" });
                await stock.SaveChangesAsync(innerToken);
                return 0;
            }, ct);
        });

        _database.Count("CatalogItems").Should().Be(1);
        _database.Count("StockItems").Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAContextIsDisposedInsideTheUnit_CommitsTheRest()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CatalogContext>>();

        await unit.ExecuteAsync(async ct =>
        {
            await using (var shortLived = new CatalogContext(options))
            {
                shortLived.Items.Add(new CatalogItem { Name = "short-lived" });
                await shortLived.SaveChangesAsync(ct);
            }

            var stock = scope.ServiceProvider.GetRequiredService<StockContext>();
            stock.Items.Add(new StockItem { Sku = "B-1" });
            await stock.SaveChangesAsync(ct);
            return 0;
        });

        _database.Count("CatalogItems").Should().Be(1);
        _database.Count("StockItems").Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAContextIsDisposedInsideAFailingUnit_RollsBackTheRest()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CatalogContext>>();

        var act = () => unit.ExecuteAsync<int>(async ct =>
        {
            await using (var shortLived = new CatalogContext(options))
            {
                shortLived.Items.Add(new CatalogItem { Name = "short-lived" });
                await shortLived.SaveChangesAsync(ct);
            }

            throw new InvalidOperationException("abandon");
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("abandon");
        _database.Count("CatalogItems").Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenDisposingTheCommittedTransactionFails_LogsItAndReportsTheCommit(bool isStoreFailure)
    {
        Exception failure = isStoreFailure ? new SqliteException("disposal failed", 1) : new InvalidOperationException("disposal failed");
        var logger = new RecordingLogger<SharedTransaction>();
        await using var transaction = NewTransaction(new FailingDisposalConnection(failure), logger);

        var result = await transaction.ExecuteAsync(_ => Task.FromResult("persisted"));

        result.Should().Be("persisted");
        logger.Warnings.Should().ContainSingle().Which.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisposingAnUncommittedTransactionFails_ThrowsIt()
    {
        var logger = new RecordingLogger<SharedTransaction>();
        await using var transaction = NewTransaction(
            new FailingDisposalConnection(new InvalidOperationException("disposal failed")), logger);

        var act = () => transaction.ExecuteAsync<int>(_ => throw new TimeoutException("work failed"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*disposal failed*");
        logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task UseBsftSharedTransaction_OnAPooledContext_RefusesTheRootProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftDataSharedTransaction(_ => new SqliteConnection(_database.ConnectionString));
        services.AddDbContextPool<CatalogContext>((sp, options) => options
            .UseBsftSharedTransaction(sp, (builder, connection) => builder.UseSqlite(connection)));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var act = () => scope.ServiceProvider.GetRequiredService<CatalogContext>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*root service provider*AddDbContextPool*");
    }

    [Fact]
    public async Task ExecuteAsync_WhenANestedFailureIsSwallowed_ReportsTheFirstOneAsInnerException()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var firstFailure = new InvalidOperationException("first nested failure");
        var secondFailure = new InvalidOperationException("second nested failure");

        var act = () => unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "outer" });
            await catalog.SaveChangesAsync(ct);

            // First nested unit throws
            var nestedOne = () => unit.ExecuteAsync<int>(
                _ => throw firstFailure, ct);
            await nestedOne.Should().ThrowAsync<InvalidOperationException>();

            // Second nested unit also throws
            var nestedTwo = () => unit.ExecuteAsync<int>(
                _ => throw secondFailure, ct);
            await nestedTwo.Should().ThrowAsync<InvalidOperationException>();

            return 0;
        });

        var ambientFailure = await act.Should().ThrowAsync<AmbientTransactionMisuseException>();
        ambientFailure.Which.InnerException.Should().BeSameAs(firstFailure);
        _database.Count("CatalogItems").Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_AfterAUnitWithASwallowedNestedFailure_StartsTheNextUnitWithoutIt()
    {
        // Classifier recognizes TimeoutException as a conflict
        await using var provider = _database.BuildProvider(services =>
            services.AddSingleton<ITransactionConflictClassifier, FakeTransactionConflictClassifier<TimeoutException>>());
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<IAmbientTransaction>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var swallowedTimeoutFailure = new TimeoutException("swallowed conflict");
        var testException = new InvalidOperationException("test");
        AmbientTransactionMisuseException? thrownMisuse = null;

        // First unit with swallowed nested failure (TimeoutException)
        var actFirst = () => unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "first" });
            await catalog.SaveChangesAsync(ct);

            var nested = () => unit.ExecuteAsync<int>(_ => throw swallowedTimeoutFailure, ct);
            await nested.Should().ThrowAsync<TimeoutException>();

            return 0;
        });

        thrownMisuse = (await actFirst.Should().ThrowAsync<AmbientTransactionMisuseException>()).Which;
        // After first unit, the nested failure is reset and no longer counts on its own
        unit.IsTransactionConflict(testException).Should().BeFalse("the nested failure ended with the unit and is no longer recorded");
        // But it is available through the thrown misuse exception's inner exception
        unit.IsTransactionConflict(thrownMisuse).Should().BeTrue("the nested failure is in the InnerException of the thrown misuse");

        // Second unit should start fresh
        await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "second" });
            await catalog.SaveChangesAsync(ct);
            return 0;
        });

        // After second unit, nested failure was reset, so unrelated exceptions don't match
        unit.IsTransactionConflict(testException).Should().BeFalse("the next unit forgot the earlier failure");
        unit.IsTransactionConflict(swallowedTimeoutFailure).Should().BeTrue("but the TimeoutException itself is still a conflict to the classifier");
        _database.Count("CatalogItems").Should().Be(1);
    }

    [Fact]
    public async Task IsTransactionConflict_WhenHandlerThrowsOwnExceptionAfterCatchingANestedConflict_HandlerExceptionIsNotConflict()
    {
        // Classifier recognizes TimeoutException as a conflict
        await using var provider = _database.BuildProvider(services =>
            services.AddSingleton<ITransactionConflictClassifier, FakeTransactionConflictClassifier<TimeoutException>>());
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<IAmbientTransaction>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var nestedConflict = new TimeoutException("nested conflict");

        var act = () => unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "item" });
            await catalog.SaveChangesAsync(ct);

            try
            {
                await unit.ExecuteAsync<int>(_ => throw nestedConflict, ct);
            }
            catch (TimeoutException)
            {
                // Handler catches the nested conflict and throws its own exception
                throw new InvalidOperationException("own failure");
            }

            return 0;
        });

        // The unit throws the handler's exception, not wrapped
        var handlerException = await act.Should().ThrowAsync<InvalidOperationException>();
        handlerException.Which.Message.Should().Be("own failure");

        // The handler's exception is not a conflict (only TimeoutException is)
        unit.IsTransactionConflict(handlerException.Which).Should().BeFalse("the handler's own exception is not a conflict, even though a nested conflict was caught");
        _database.Count("CatalogItems").Should().Be(0);
    }

    [Fact]
    public async Task IsTransactionConflict_ForARefusedOverlappingUnit_IsFalseEvenWhenTheRunningUnitRecordedAConflict()
    {
        // The classifier calls the command failure the running unit swallows a conflict.
        await using var provider = _database.BuildProvider(services =>
            services.AddSingleton<ITransactionConflictClassifier, FakeTransactionConflictClassifier<SqliteException>>());
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<IAmbientTransaction>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var recorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Flow A runs the outermost unit, swallows a failed command and waits.
        var flow = unit.ExecuteAsync(async ct =>
        {
            try
            {
                await catalog.Database.ExecuteSqlRawAsync("SELECT * FROM NoSuchTable", ct);
            }
            catch (SqliteException)
            {
                // Swallowed: the interceptor recorded it as the unit's command failure.
            }

            recorded.SetResult();
            await release.Task;
            return 0;
        });

        await recorded.Task;

        // Flow B starts another outermost unit in the same scope and is refused.
        var refusal = await Assert.ThrowsAsync<AmbientTransactionMisuseException>(
            () => Task.Run(() => unit.ExecuteAsync(_ => Task.FromResult(0))));
        refusal.Message.Should().Contain("one unit of work at a time");

        // Counter-check: any other failure counts as a conflict through the recorded command failure.
        unit.IsTransactionConflict(new InvalidOperationException()).Should().BeTrue();

        // The refusal is never one: a fresh scope would hide the overlap.
        unit.IsTransactionConflict(refusal).Should().BeFalse();
        unit.FindTransactionConflict(refusal).Should().BeNull();

        release.SetResult();
        await FluentActions.Awaiting(() => flow).Should().ThrowAsync<AmbientTransactionMisuseException>();
    }

    [Fact]
    public async Task FindTransactionConflict_ReturnsTheLinkTheClassifierCalledAConflict()
    {
        // Classifier recognizes TimeoutException as a conflict
        await using var provider = _database.BuildProvider(services =>
            services.AddSingleton<ITransactionConflictClassifier, FakeTransactionConflictClassifier<TimeoutException>>());
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<IAmbientTransaction>();

        var timeoutConflict = new TimeoutException("conflict");
        var wrapped = new InvalidOperationException("outer", new AmbientTransactionMisuseException("wrapped", timeoutConflict));

        // FindTransactionConflict returns the exact link that the classifier recognized
        unit.FindTransactionConflict(wrapped).Should().BeSameAs(timeoutConflict);

        // When there is no conflict, it returns null
        var unrelated = new AmbientTransactionMisuseException("wrapped", new SqliteException("unrelated", 1));
        unit.FindTransactionConflict(unrelated).Should().BeNull();

        // With no argument, it throws
        FluentActions.Invoking(() => unit.FindTransactionConflict(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task IsTransactionConflict_WhenTheConflictIsAnInnerException_AsksTheClassifierForEveryLink()
    {
        // The classifier inspects only the exception it is given and walks no chain itself.
        await using var provider = _database.BuildProvider(services =>
            services.AddSingleton<ITransactionConflictClassifier, FakeTransactionConflictClassifier<TimeoutException>>());
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<IAmbientTransaction>();

        var wrapped = new InvalidOperationException("outer", new AmbientTransactionMisuseException("wrapped", new TimeoutException("conflict")));
        var unrelated = new AmbientTransactionMisuseException("wrapped", new SqliteException("unrelated", 1));

        unit.IsTransactionConflict(wrapped).Should().BeTrue("the conflict two links down the chain is still found");
        unit.IsTransactionConflict(unrelated).Should().BeFalse("no link of the chain is a conflict");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheCallerCancelsAfterTheWork_SavesPendingChangesAndCommits()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        using var cts = new CancellationTokenSource();

        await unit.ExecuteAsync(ct =>
        {
            // No SaveChanges: the entity is only tracked, so the outermost unit saves it after the work.
            catalog.Items.Add(new CatalogItem { Name = "unsaved" });

            // The caller cancels as the work completes.
            cts.Cancel();
            ct.IsCancellationRequested.Should().BeTrue();

            return Task.FromResult(0);
        }, cts.Token);

        // The unsaved changes should have been saved and committed despite the cancellation
        _database.Count("CatalogItems").Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheCallerCancelsBeforeASaveInTheWork_CommitsNothing()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        using var cts = new CancellationTokenSource();

        var act = () => unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "unsaved" });

            // Cancel before SaveChanges
            cts.Cancel();

            // This should throw OperationCanceledException
            await catalog.SaveChangesAsync(ct);

            return 0;
        }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _database.Count("CatalogItems").Should().Be(0);
    }

    public void Dispose() => _database.Dispose();

    private static SharedTransaction NewTransaction(DbConnection connection, ILogger<SharedTransaction>? logger = null)
    {
        var enlistment = new SharedTransactionEnlistment();

        return new SharedTransaction(
            connection,
            new SharedContextProvider(new ServiceCollection().BuildServiceProvider(), enlistment),
            Options.Create(new BsftDataOptions()),
            new SharedTransactionHealth(enlistment, [], []),
            new SharedTransactionStepReporter([], NullLogger<SharedTransactionStepReporter>.Instance),
            logger ?? NullLogger<SharedTransaction>.Instance);
    }

    /// <summary>An execution strategy that says it retries, the way <c>EnableRetryOnFailure</c> does.</summary>
    private sealed class RetryingStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }
}
