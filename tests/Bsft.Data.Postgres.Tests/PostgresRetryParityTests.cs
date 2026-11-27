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
/// Retry parity: serialization failures and deadlocks are retried, but other exceptions are not.
/// Existing test SendAsync_WhenTheNestedCommandHitsASerializationFailure_RetriesTheWholeOutermostUnit
/// and others in PostgresSharedTransactionTests prove that nested command failures retry as part of outer unit.
/// </summary>
/// <remarks>
/// Every fact is reported as skipped without a server; set <c>BSFT_TEST_POSTGRES</c> to run them.
/// </remarks>
public sealed class PostgresRetryParityTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    [PostgresFact]
    public async Task ExecuteAsync_WhenADeadlockOccurs_IsRetried()
    {
        var connectionString = await fixture.CreateDatabaseAsync("retry_deadlock");
        var attempts = new RetryCountingLog { FailureMode = "deadlock", FailuresBeforeSuccess = 1 };
        await using var provider = BuildProvider(connectionString, attempts, shouldRetry: (_) => false);
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IMediator>()
            .SendAsync(new AlwaysFailCommand("deadlock"));

        attempts.CommandAttempts.Should().Be(2, "deadlock 40P01 is retried exactly once (2 total runs)");
    }

    [PostgresFact]
    public async Task ExecuteAsync_WhenARequestIsRetried_RunsInAFreshDiScope()
    {
        var connectionString = await fixture.CreateDatabaseAsync("retry_scope");
        var attempts = new RetryCountingLog { FailureMode = "serialization", FailuresBeforeSuccess = 1 };
        await using var provider = BuildProvider(connectionString, attempts, shouldRetry: (_) => false);
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .SendAsync(new CheckScopeCommand());

        // If the second attempt ran in a fresh scope, the context instances should be different,
        // and the ChangeTracker should be empty at the start of the retry
        result.Should().Be("fresh_scope", "each retry runs in a fresh DI scope with a new DbContext instance");
        attempts.HasFreshChangeTracker.Should().BeTrue("the DbContext's ChangeTracker should be empty at the start of attempt 2");
    }

    [PostgresFact]
    public async Task SendAsync_WhenTheConflictPersists_RunsTheHandlerThreeTimesWithTwoRetries()
    {
        var connectionString = await fixture.CreateDatabaseAsync("conflict_persists");
        var attempts = new RetryCountingLog { FailureMode = "serialization", FailuresBeforeSuccess = int.MaxValue };
        await using var provider = BuildProvider(connectionString, attempts, shouldRetry: (_) => false, maxRetryAttempts: 2);
        await using var scope = provider.CreateAsyncScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IMediator>()
            .SendAsync(new AlwaysFailSerializationCommand());

        var exception = await act.Should().ThrowAsync<Exception>();

        // Verify that we get a PostgresException with the right SqlState somewhere in the chain
        var currentException = exception.Which as Exception;
        var foundSerializationError = false;
        while (currentException is not null)
        {
            if (currentException is PostgresException postgresEx && postgresEx.SqlState == "40001")
            {
                foundSerializationError = true;
                break;
            }
            currentException = currentException.InnerException;
        }

        foundSerializationError.Should().BeTrue("the exception chain should contain a PostgresException with SqlState 40001");
        attempts.CommandAttempts.Should().Be(3, "with MaxRetryAttempts = 2, the handler runs 1 initial + 2 retries = 3 times");
    }

    [PostgresFact]
    public async Task SendAsync_WhenAUniqueViolationEscapes_RunsTheHandlerOnce()
    {
        var connectionString = await fixture.CreateDatabaseAsync("unique_violation");
        var attempts = new RetryCountingLog();
        await using var provider = BuildProvider(connectionString, attempts, shouldRetry: (_) => false);
        await using var scope = provider.CreateAsyncScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IMediator>()
            .SendAsync(new UniqueViolationCommand());

        var exception = await act.Should().ThrowAsync<Exception>();
        exception.Which.Should().BeOfType<DbUpdateException>();

        // Verify that we get a PostgresException with the unique violation error code
        var currentException = exception.Which as Exception;
        var foundUniqueError = false;
        while (currentException is not null)
        {
            if (currentException is PostgresException postgresEx && postgresEx.SqlState == "23505")
            {
                foundUniqueError = true;
                break;
            }
            currentException = currentException.InnerException;
        }

        foundUniqueError.Should().BeTrue("the exception chain should contain a PostgresException with SqlState 23505 (unique violation)");
        attempts.CommandAttempts.Should().Be(1, "unique violations are not retried");
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        RetryCountingLog log,
        Func<Exception?, bool> shouldRetry,
        int? maxRetryAttempts = null)
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
            if (maxRetryAttempts.HasValue)
            {
                options.MaxRetryAttempts = maxRetryAttempts.Value;
            }
        });
        services.AddBsftDataPostgresTransaction(connectionString);
        services.Configure<BsftDataOptions>(options => options.SharedTransaction.IsolationLevel = IsolationLevel.Serializable);
        services.AddDbContext<CatalogContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
        services.AddDbContext<InventoryContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory")));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
