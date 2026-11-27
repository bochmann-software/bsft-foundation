using System.Data;
using Bsft.Core.Persistence;
using Bsft.Data.Tests.Fakes;
using Bsft.Data.Tests.Support;
using Bsft.Data.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Tests.Transactions;

/// <summary>
/// What an <see cref="ISharedTransactionStepObserver"/> is told about the steps the shared transaction runs
/// on the connection outside EF Core, in which order, and what its exceptions do to the unit.
/// </summary>
public sealed class SharedTransactionObserverTests : IDisposable
{
    private readonly SharedTransactionDatabase _database = new();
    private readonly List<string> _log = [];
    private readonly RecordingLogger<SharedTransactionStepReporter> _logger = new();

    [Fact]
    public async Task ExecuteAsync_WhenTheUnitCommits_ReportsOpenBeginCommitReleaseAndCloseInOrder()
    {
        var observer = new FakeSharedTransactionStepObserver(_log);
        await using var provider = BuildProvider(observer);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        await unit.ExecuteAsync(async ct =>
        {
            _log.Add("work");
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);

            return 0;
        });

        _log.Should().Equal(
            "+Open", "-Open", "+Begin", "-Begin", "work", "+Commit", "-Commit", "+Release", "-Release", "+Close", "-Close");
        observer.Steps.Should().OnlyContain(step => ReferenceEquals(step.Connection, catalog.Database.GetDbConnection()),
            "every step runs on the scope's shared connection");
        _database.Count("CatalogItems").Should().Be(1);
        _logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheWorkFails_ReportsOpenBeginRollbackAndClose()
    {
        await using var provider = BuildProvider(new FakeSharedTransactionStepObserver(_log));
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var act = () => unit.ExecuteAsync<int>(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);

            throw new TimeoutException("work failed");
        });

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        _log.Should().Equal("+Open", "-Open", "+Begin", "-Begin", "+Rollback", "-Rollback", "+Close", "-Close");
        _database.Count("CatalogItems").Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithAnAbortProbe_ReportsTheProbeBetweenBeginAndCommit()
    {
        var probe = new FakeTransactionAbortProbe(abort: null);
        await using var provider = BuildProvider(
            new FakeSharedTransactionStepObserver(_log), services => services.AddSingleton<ITransactionAbortProbe>(probe));
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();

        await unit.ExecuteAsync(_ => Task.FromResult(0));

        _log.Should().Equal(
            "+Open", "-Open", "+Begin", "-Begin", "+AbortProbe", "-AbortProbe", "+Commit", "-Commit",
            "+Release", "-Release", "+Close", "-Close");
        probe.Probes.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheProbeFindsTheTransactionAborted_CompletesTheProbeStepAndRollsBack()
    {
        var probe = new FakeTransactionAbortProbe(new TimeoutException("aborted"));
        await using var provider = BuildProvider(
            new FakeSharedTransactionStepObserver(_log), services => services.AddSingleton<ITransactionAbortProbe>(probe));
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();

        var act = () => unit.ExecuteAsync(_ => Task.FromResult(0));

        await act.Should().ThrowAsync<AmbientTransactionMisuseException>();
        _log.Should().Equal(
            "+Open", "-Open", "+Begin", "-Begin", "+AbortProbe", "-AbortProbe", "+Rollback", "-Rollback", "+Close", "-Close");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheConnectionIsAlreadyOpen_ReportsNeitherOpenNorClose()
    {
        await using var provider = BuildProvider(new FakeSharedTransactionStepObserver(_log));
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var connection = scope.ServiceProvider.GetRequiredService<CatalogContext>().Database.GetDbConnection();
        await connection.OpenAsync();

        await unit.ExecuteAsync(_ => Task.FromResult(0));

        _log.Should().Equal("+Begin", "-Begin", "+Commit", "-Commit", "+Release", "-Release");
        connection.State.Should().Be(ConnectionState.Open);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNested_ReportsOnlyTheStepsOfTheOutermostUnit()
    {
        await using var provider = BuildProvider(new FakeSharedTransactionStepObserver(_log));
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();

        await unit.ExecuteAsync(ct => unit.ExecuteAsync(_ => Task.FromResult(0), ct));

        _log.Should().Equal(
            "+Open", "-Open", "+Begin", "-Begin", "+Commit", "-Commit", "+Release", "-Release", "+Close", "-Close");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnObserverRefusesTheOpen_DoesNotTouchTheConnectionAndThrowsTheRefusal()
    {
        var refusal = new GuardRefusalException();
        var observer = new FakeSharedTransactionStepObserver(_log) { Refusals = { [SharedTransactionStepKind.Open] = refusal } };
        await using var provider = BuildProvider(observer);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var connection = scope.ServiceProvider.GetRequiredService<CatalogContext>().Database.GetDbConnection();
        var isWorkRun = false;

        var act = () => unit.ExecuteAsync(_ =>
        {
            isWorkRun = true;

            return Task.FromResult(0);
        });

        (await act.Should().ThrowExactlyAsync<GuardRefusalException>()).Which.Should().BeSameAs(refusal);
        _log.Should().Equal("+Open");
        isWorkRun.Should().BeFalse();
        connection.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnObserverRefusesTheBegin_ClosesTheConnectionAndThrowsTheRefusal()
    {
        var refusal = new GuardRefusalException();
        var observer = new FakeSharedTransactionStepObserver(_log) { Refusals = { [SharedTransactionStepKind.Begin] = refusal } };
        await using var provider = BuildProvider(observer);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var act = () => unit.ExecuteAsync(_ => Task.FromResult(0));

        (await act.Should().ThrowExactlyAsync<GuardRefusalException>()).Which.Should().BeSameAs(refusal);
        _log.Should().Equal("+Open", "-Open", "+Begin", "+Close", "-Close");
        catalog.Database.CurrentTransaction.Should().BeNull("no transaction was begun");
        catalog.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnObserverRefusesTheAbortProbe_DoesNotProbeAndRollsBack()
    {
        var refusal = new GuardRefusalException();
        var probe = new FakeTransactionAbortProbe(abort: null);
        var observer = new FakeSharedTransactionStepObserver(_log)
        {
            Refusals = { [SharedTransactionStepKind.AbortProbe] = refusal },
        };
        await using var provider = BuildProvider(observer, services => services.AddSingleton<ITransactionAbortProbe>(probe));
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var act = () => unit.ExecuteAsync(ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });

            return Task.FromResult(0);
        });

        (await act.Should().ThrowExactlyAsync<GuardRefusalException>()).Which.Should().BeSameAs(refusal);
        probe.Probes.Should().Be(0);
        _log.Should().Equal("+Open", "-Open", "+Begin", "-Begin", "+AbortProbe", "+Rollback", "-Rollback", "+Close", "-Close");
        _database.Count("CatalogItems").Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnObserverRefusesTheCommit_RollsBackAndThrowsTheRefusalUnwrapped()
    {
        var refusal = new GuardRefusalException();
        var observer = new FakeSharedTransactionStepObserver(_log) { Refusals = { [SharedTransactionStepKind.Commit] = refusal } };
        await using var provider = BuildProvider(observer);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var act = () => unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);

            return 0;
        });

        (await act.Should().ThrowExactlyAsync<GuardRefusalException>()).Which.Should().BeSameAs(refusal);
        _log.Should().Equal("+Open", "-Open", "+Begin", "-Begin", "+Commit", "+Rollback", "-Rollback", "+Close", "-Close");
        _database.Count("CatalogItems").Should().Be(0);
        catalog.ChangeTracker.Entries().Should().BeEmpty("a refused commit ends the unit like a failed one");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnObserverRefusesTheRollback_StillReleasesAndKeepsTheUnitsException()
    {
        var refusal = new GuardRefusalException();
        var observer = new FakeSharedTransactionStepObserver(_log) { Refusals = { [SharedTransactionStepKind.Rollback] = refusal } };
        await using var provider = BuildProvider(observer);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var act = () => unit.ExecuteAsync<int>(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "lost" });
            await catalog.SaveChangesAsync(ct);

            throw new TimeoutException("work failed");
        });

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        _log.Should().Equal("+Open", "-Open", "+Begin", "-Begin", "+Rollback", "+Close", "-Close");
        _logger.Warnings.Should().ContainSingle().Which.Should().BeSameAs(refusal);
        catalog.Database.CurrentTransaction.Should().BeNull("the transaction is released although the rollback was refused");
        _database.Count("CatalogItems").Should().Be(0);

        await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "kept" });
            await catalog.SaveChangesAsync(ct);

            return 0;
        });

        _database.Count("CatalogItems").Should().Be(1, "the connection is free for the next unit");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnObserverRefusesTheReleaseAfterTheCommit_SucceedsLogsTheRefusalAndPersistsOnce()
    {
        var refusal = new GuardRefusalException();
        var observer = new FakeSharedTransactionStepObserver(_log) { Refusals = { [SharedTransactionStepKind.Release] = refusal } };
        await using var provider = BuildProvider(observer);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var result = await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);

            return "persisted";
        });

        result.Should().Be("persisted", "a committed unit is never reported as failed");
        _log.Should().Equal("+Open", "-Open", "+Begin", "-Begin", "+Commit", "-Commit", "+Release", "+Close", "-Close");
        _database.Count("CatalogItems").Should().Be(1, "the unit persisted exactly once");
        _logger.Warnings.Should().ContainSingle().Which.Should().BeSameAs(refusal);
        catalog.Database.CurrentTransaction.Should().BeNull("the transaction is released although the release was refused");
        catalog.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnObserverRefusesTheCloseOfACommittedUnit_ClosesSucceedsAndLogsTheRefusal()
    {
        var refusal = new GuardRefusalException();
        var observer = new FakeSharedTransactionStepObserver(_log) { Refusals = { [SharedTransactionStepKind.Close] = refusal } };
        await using var provider = BuildProvider(observer);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var connection = scope.ServiceProvider.GetRequiredService<CatalogContext>().Database.GetDbConnection();

        var result = await unit.ExecuteAsync(_ => Task.FromResult(7));

        result.Should().Be(7);
        _log.Should().Equal("+Open", "-Open", "+Begin", "-Begin", "+Commit", "-Commit", "+Release", "-Release", "+Close");
        connection.State.Should().Be(ConnectionState.Closed);
        _logger.Warnings.Should().ContainSingle().Which.Should().BeSameAs(refusal);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnObserverRefusesTheCloseOfAFailedUnit_ClosesAndLogsTheRefusal()
    {
        var refusal = new GuardRefusalException();
        var observer = new FakeSharedTransactionStepObserver(_log) { Refusals = { [SharedTransactionStepKind.Close] = refusal } };
        await using var provider = BuildProvider(observer);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var connection = scope.ServiceProvider.GetRequiredService<CatalogContext>().Database.GetDbConnection();

        var act = () => unit.ExecuteAsync<int>(_ => throw new TimeoutException("work failed"));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        connection.State.Should().Be(ConnectionState.Closed);
        _logger.Warnings.Should().ContainSingle().Which.Should().BeSameAs(refusal);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnObserverFailsOnCompletion_LogsEveryFailureAndCommits()
    {
        var failure = new GuardRefusalException();
        var observer = new FakeSharedTransactionStepObserver(_log) { CompletionFailure = failure };
        await using var provider = BuildProvider(observer);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);

            return 0;
        });

        _database.Count("CatalogItems").Should().Be(1);
        _logger.Warnings.Should().HaveCount(5).And.OnlyContain(warning => ReferenceEquals(warning, failure));
    }

    [Fact]
    public async Task ExecuteAsync_WhenTakingTheTransactionBackFailsAndTheRollbackIsRefused_RollsBackAndLogsTheRefusal()
    {
        var failure = new InvalidOperationException("resolving a context failed");
        var isFailing = false;
        using var serviceProvider = new ServiceCollection()
            .AddTransient(_ => isFailing ? throw failure : new RecordedService())
            .BuildServiceProvider();
        var enlistment = new SharedTransactionEnlistment();
        var contexts = new SharedContextProvider(serviceProvider, enlistment);
        contexts.Record(typeof(RecordedService));
        var refusal = new GuardRefusalException();
        var observer = new FakeSharedTransactionStepObserver(_log) { Refusals = { [SharedTransactionStepKind.Rollback] = refusal } };
        var connection = new SqliteConnection(_database.ConnectionString);
        await using var transaction = new SharedTransaction(
            connection,
            contexts,
            Options.Create(new BsftDataOptions()),
            new SharedTransactionHealth(enlistment, [], []),
            new SharedTransactionStepReporter([observer], _logger),
            NullLogger<SharedTransaction>.Instance);

        var act = () => transaction.ExecuteAsync(_ =>
        {
            isFailing = true;

            return Task.FromResult(0);
        });

        (await act.Should().ThrowExactlyAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        _log.Should().Equal("+Open", "-Open", "+Begin", "-Begin", "+Rollback", "+Close", "-Close");
        _logger.Warnings.Should().ContainSingle().Which.Should().BeSameAs(refusal);
        connection.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task AddBsftDataSharedTransaction_WithTwoObservers_TellsThemInRegistrationOrderAndCompletesOnlyTheAdmitting()
    {
        var refusal = new GuardRefusalException();
        var first = new FakeSharedTransactionStepObserver(_log, "a");
        var second = new FakeSharedTransactionStepObserver(_log, "b") { Refusals = { [SharedTransactionStepKind.Commit] = refusal } };
        await using var provider = _database.BuildProvider(services =>
        {
            services.AddScoped<ISharedTransactionStepObserver>(_ => first);
            services.AddScoped<ISharedTransactionStepObserver>(_ => second);
            services.AddSingleton<ILogger<SharedTransactionStepReporter>>(_logger);
        });
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var act = () => unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);

            return 0;
        });

        (await act.Should().ThrowExactlyAsync<GuardRefusalException>()).Which.Should().BeSameAs(refusal);
        _log.Should().Equal(
            "a+Open", "b+Open", "b-Open", "a-Open",
            "a+Begin", "b+Begin", "b-Begin", "a-Begin",
            "a+Commit", "b+Commit", "a-Commit",
            "a+Rollback", "b+Rollback", "b-Rollback", "a-Rollback",
            "a+Close", "b+Close", "b-Close", "a-Close");
        _database.Count("CatalogItems").Should().Be(0, "the commit the second observer refused never ran");
        _logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task AddBsftDataSharedTransaction_WithoutAnObserver_RunsTheUnitAndLogsNothing()
    {
        await using var provider = _database.BuildProvider(services =>
            services.AddSingleton<ILogger<SharedTransactionStepReporter>>(_logger));
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        await unit.ExecuteAsync(async ct =>
        {
            catalog.Items.Add(new CatalogItem { Name = "bolt" });
            await catalog.SaveChangesAsync(ct);

            return 0;
        });

        _database.Count("CatalogItems").Should().Be(1);
        _logger.Warnings.Should().BeEmpty();
    }

    public void Dispose() => _database.Dispose();

    private ServiceProvider BuildProvider(
        ISharedTransactionStepObserver observer, Action<IServiceCollection>? configure = null) =>
        _database.BuildProvider(services =>
        {
            services.AddScoped(_ => observer);
            services.AddSingleton<ILogger<SharedTransactionStepReporter>>(_logger);
            configure?.Invoke(services);
        });

    /// <summary>A service recorded like a context type, whose resolution the test makes fail.</summary>
    private sealed class RecordedService;

    /// <summary>A host guard's refusal, an <see cref="InvalidOperationException"/> like a consuming application's.</summary>
    private sealed class GuardRefusalException() : InvalidOperationException("Another operation of this scope is on the connection.");
}
