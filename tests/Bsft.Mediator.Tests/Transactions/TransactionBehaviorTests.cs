using Bsft.Core;
using Bsft.Mediator.Transactions;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Reflection;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

public class TransactionBehaviorTests
{
    private static ILogger<TransactionBehavior<TRequest, TResponse>> CreateLogger<TRequest, TResponse>()
        => Substitute.For<ILogger<TransactionBehavior<TRequest, TResponse>>>();

    [Fact]
    public void Constructor_NullServiceProvider_ThrowsArgumentNullException()
    {
        // Exercises TransactionBehavior<,>.ctor line 26: null serviceProvider guard.
        var logger = CreateLogger<TestCommand, string>();

        var act = () => new TransactionBehavior<TestCommand, string>(null!, logger);

        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Exercises TransactionBehavior<,>.ctor line 27: null logger guard.
        var serviceProvider = Substitute.For<IServiceProvider>();

        var act = () => new TransactionBehavior<TestCommand, string>(serviceProvider, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task HandleAsync_WhenNoTransactionalHandler_CallsNextDirectly()
    {
        // Exercises TransactionBehavior<,>.HandleAsync line 34:
        // RequiresTransaction() returns false -> calls next() directly.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<ICommandHandler<NoTransactionCommand, string>, NoTransactionCommandHandler>();
        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILogger<TransactionBehavior<NoTransactionCommand, string>>>();
        var behavior = new TransactionBehavior<NoTransactionCommand, string>(sp, logger);

        var nextCalled = false;
        Func<Task<string>> next = () =>
        {
            nextCalled = true;
            return Task.FromResult("next-result");
        };

        var result = await behavior.HandleAsync(new NoTransactionCommand("test"), next);

        result.Should().Be("next-result");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenTransactionalHandler_AndNoEfCoreAvailable_CallsNext()
    {
        // Exercises TransactionBehavior<,>.HandleAsync lines 40-46:
        // RequiresTransaction() = true, but dbContextType is null or no DbContext registered.
        // In this test context, EF Core may or may not be loaded. Either way we test the behavior
        // of the behavior when no DbContext is registered in the service provider.
        var services = new ServiceCollection();
        services.AddLogging();
        // Register the transactional handler so RequiresTransaction() finds it
        services.AddTransient<ICommandHandler<TransactionalTestCommand, string>, TransactionalTestCommandHandler>();
        // Do NOT register a DbContext
        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILogger<TransactionBehavior<TransactionalTestCommand, string>>>();
        var behavior = new TransactionBehavior<TransactionalTestCommand, string>(sp, logger);

        var nextCalled = false;
        Func<Task<string>> next = () =>
        {
            nextCalled = true;
            return Task.FromResult("transactional-result");
        };

        // Whether EF Core type can be resolved or not, no DbContext is registered so it falls through
        var result = await behavior.HandleAsync(new TransactionalTestCommand("tx"), next);

        result.Should().Be("transactional-result");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenHandlerHasNonGenericInterface_RequiresTransactionStillWorks()
    {
        // Exercises TransactionBehavior.RequiresTransaction line 161:
        // DisposableTransactionalCommandHandler implements both ICommandHandler<,> (generic)
        // and IDisposable (non-generic). When iterating interfaces, the non-generic IDisposable
        // triggers the "if (!iface.IsGenericType) continue;" branch (line 161).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<ICommandHandler<DisposableTransactionalCommand, string>, DisposableTransactionalCommandHandler>();
        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILogger<TransactionBehavior<DisposableTransactionalCommand, string>>>();
        var behavior = new TransactionBehavior<DisposableTransactionalCommand, string>(sp, logger);

        var nextCalled = false;
        Func<Task<string>> next = () =>
        {
            nextCalled = true;
            return Task.FromResult("result");
        };

        // RequiresTransaction() will find DisposableTransactionalCommandHandler and encounter
        // IDisposable (non-generic) -> hits line 161 (continue), then ICommandHandler<,> (generic) -> returns true.
        // No DbContext registered, so falls through to next().
        var result = await behavior.HandleAsync(new DisposableTransactionalCommand("test"), next);

        result.Should().Be("result");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenTransactionalHandler_AndDbContextRegistered_ExecutesWithinTransaction()
    {
        // Exercises TransactionBehavior<,>.HandleAsync lines 48-119:
        // RequiresTransaction() = true, dbContextType is non-null (EF Core is loaded),
        // and a DbContext IS registered -> begins transaction, calls next, commits, disposes.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<ICommandHandler<TransactionalTestCommand, string>, TransactionalTestCommandHandler>();

        // Register a real EF Core DbContext backed by SQLite (which supports transactions)
        // so GetService(dbContextType) returns non-null and BeginTransactionAsync succeeds.
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContext<TransactionTestDbContext>(opt => opt.UseSqlite(connection));
        services.AddSingleton(connection); // keep connection open for lifetime of the test

        // Also register it as the base DbContext type so TransactionBehavior can resolve it
        services.AddTransient<DbContext>(sp => sp.GetRequiredService<TransactionTestDbContext>());

        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILogger<TransactionBehavior<TransactionalTestCommand, string>>>();
        var behavior = new TransactionBehavior<TransactionalTestCommand, string>(sp, logger);

        var nextCalled = false;
        Func<Task<string>> next = () =>
        {
            nextCalled = true;
            return Task.FromResult("transactional-result");
        };

        var result = await behavior.HandleAsync(new TransactionalTestCommand("tx"), next);

        result.Should().Be("transactional-result");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenTransactionalHandler_AndDbContextRegistered_ExecutesWithinTransaction_AsyncNext()
    {
        // Exercises TransactionBehavior<,>.HandleAsync with a genuinely async next() delegate.
        // This ensures the async state machine's suspension/resume branches are covered
        // in addition to the synchronous-completion fast path.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<ICommandHandler<TransactionalTestCommand, string>, TransactionalTestCommandHandler>();

        var connection3 = new SqliteConnection("DataSource=:memory:");
        connection3.Open();
        services.AddDbContext<TransactionTestDbContext>(opt => opt.UseSqlite(connection3));
        services.AddSingleton(connection3);
        services.AddTransient<DbContext>(sp => sp.GetRequiredService<TransactionTestDbContext>());

        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILogger<TransactionBehavior<TransactionalTestCommand, string>>>();
        var behavior = new TransactionBehavior<TransactionalTestCommand, string>(sp, logger);

        // Use Task.Yield() to force actual async suspension, covering the state machine resume branch
        Func<Task<string>> asyncNext = async () =>
        {
            await Task.Yield();
            return "async-transactional-result";
        };

        var result = await behavior.HandleAsync(new TransactionalTestCommand("tx"), asyncNext);

        result.Should().Be("async-transactional-result");
    }

    [Fact]
    public async Task HandleAsync_WhenTransactionalHandler_AndDbContextRegistered_RollsBackOnException()
    {
        // Exercises TransactionBehavior<,>.HandleAsync catch block (lines 97-108):
        // next() throws -> rollback is executed, then exception is re-thrown.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<ICommandHandler<TransactionalTestCommand, string>, TransactionalTestCommandHandler>();
        var connection2 = new SqliteConnection("DataSource=:memory:");
        connection2.Open();
        services.AddDbContext<TransactionTestDbContext>(opt => opt.UseSqlite(connection2));
        services.AddSingleton(connection2);
        services.AddTransient<DbContext>(sp => sp.GetRequiredService<TransactionTestDbContext>());

        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILogger<TransactionBehavior<TransactionalTestCommand, string>>>();
        var behavior = new TransactionBehavior<TransactionalTestCommand, string>(sp, logger);

        Func<Task<string>> next = () => throw new InvalidOperationException("simulated failure");

        var act = async () => await behavior.HandleAsync(new TransactionalTestCommand("tx"), next);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("simulated failure");
    }
}
