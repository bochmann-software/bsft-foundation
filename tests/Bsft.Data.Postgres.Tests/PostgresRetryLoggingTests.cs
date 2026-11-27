using Bsft.Data;
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
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bsft.Data.Postgres.Tests;

/// <summary>
/// Verifies that retry logging works end-to-end with the Postgres data package and the mediator resilience pipeline.
/// </summary>
/// <remarks>
/// Every fact is reported as skipped without a server; set <c>BSFT_TEST_POSTGRES</c> to run them.
/// </remarks>
public sealed class PostgresRetryLoggingTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    [PostgresFact]
    public async Task ExecuteAsync_WhenADeadlockOccurs_LogsOneWarningWithCorrectSqlState()
    {
        var connectionString = await fixture.CreateDatabaseAsync("retry_logging_deadlock");
        var attempts = new RetryCountingLog { FailureMode = "deadlock", FailuresBeforeSuccess = 1 };

        var logEntries = new List<LogEntry>();
        var loggerProvider = new TestLoggerProvider(logEntries);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(loggerProvider));
        services.AddSingleton(attempts);
        services.AddBsftMediator(mediator => mediator
            .RegisterServicesFromAssembly(typeof(PlaceOrderCommandHandler).Assembly)
            .UseDefaults());
        services.AddBsftResilienceMediator(options =>
        {
            options.BaseDelay = System.TimeSpan.Zero;
            options.ShouldRetry = (_) => false;
        });
        services.AddBsftDataPostgresTransaction(connectionString);
        services.Configure<BsftDataOptions>(options =>
            options.SharedTransaction.IsolationLevel = System.Data.IsolationLevel.ReadCommitted);
        services.AddDbContext<CatalogContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
        services.AddDbContext<InventoryContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory")));

        await using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = serviceProvider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IMediator>()
            .SendAsync(new AlwaysFailCommand("deadlock"));

        attempts.CommandAttempts.Should().Be(2, "deadlock 40P01 is retried exactly once (2 total runs)");

        var retryEntries = logEntries.Where(e =>
            e.Category == "Bsft.Resilience.Mediator.MediatorResiliencePipelineProvider" &&
            e.LogLevel == LogLevel.Warning &&
            e.EventId.Id == 1).ToList();

        retryEntries.Should().ContainSingle("exactly one retry warning should be logged for the deadlock");

        var entry = retryEntries[0];
        entry.EventId.Name.Should().Be("MediatorRequestRetrying");

        var sqlStateValue = GetStateValue(entry.State, "SqlState");
        sqlStateValue.Should().Be("40P01", "deadlock error code is 40P01");

        var retryAttemptValue = GetStateValue(entry.State, "RetryAttempt");
        retryAttemptValue.Should().Be("1", "first retry is attempt 1");

        var requestTypeValue = GetStateValue(entry.State, "RequestType");
        requestTypeValue.Should().Be(typeof(AlwaysFailCommand).FullName);
    }

    [PostgresFact]
    public async Task ExecuteAsync_WhenNoFailureOccurs_LogsNothing()
    {
        var connectionString = await fixture.CreateDatabaseAsync("retry_logging_success");
        var attempts = new RetryCountingLog { FailureMode = "deadlock", FailuresBeforeSuccess = 0 };

        var logEntries = new List<LogEntry>();
        var loggerProvider = new TestLoggerProvider(logEntries);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(loggerProvider));
        services.AddSingleton(attempts);
        services.AddBsftMediator(mediator => mediator
            .RegisterServicesFromAssembly(typeof(PlaceOrderCommandHandler).Assembly)
            .UseDefaults());
        services.AddBsftResilienceMediator(options =>
        {
            options.BaseDelay = System.TimeSpan.Zero;
            options.ShouldRetry = (_) => false;
        });
        services.AddBsftDataPostgresTransaction(connectionString);
        services.Configure<BsftDataOptions>(options =>
            options.SharedTransaction.IsolationLevel = System.Data.IsolationLevel.ReadCommitted);
        services.AddDbContext<CatalogContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
        services.AddDbContext<InventoryContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory")));

        await using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = serviceProvider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IMediator>()
            .SendAsync(new AlwaysFailCommand("deadlock"));

        attempts.CommandAttempts.Should().Be(1);

        var retryEntries = logEntries.Where(e =>
            e.Category == "Bsft.Resilience.Mediator.MediatorResiliencePipelineProvider" &&
            e.LogLevel == LogLevel.Warning &&
            e.EventId.Id == 1).ToList();

        retryEntries.Should().BeEmpty("no retry logging should occur when there are no failures");
    }

    [PostgresFact]
    public async Task SendAsync_WhenTheHandlerSwallowsADeadlockAndFailsWithAbortedTransaction_LogsTheDeadlockSqlState()
    {
        var connectionString = await fixture.CreateDatabaseAsync("retry_logging_swallowed_deadlock");
        var log = new ScenarioLog
        {
            ConnectionString = connectionString,
            SwallowedDeadlocks = 1,
            IsWritingAfterTheSwallowedDeadlock = true,
        };

        var logEntries = new List<LogEntry>();
        var loggerProvider = new TestLoggerProvider(logEntries);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(loggerProvider));
        services.AddSingleton(log);
        services.AddBsftMediator(mediator => mediator
            .RegisterServicesFromAssembly(typeof(PlaceOrderCommandHandler).Assembly)
            .UseDefaults());
        services.AddBsftResilienceMediator(options =>
        {
            options.BaseDelay = System.TimeSpan.Zero;
            options.ShouldRetry = _ => false;
        });
        services.AddBsftDataPostgresTransaction(connectionString);
        services.Configure<BsftDataOptions>(options =>
            options.SharedTransaction.IsolationLevel = System.Data.IsolationLevel.ReadCommitted);
        services.AddDbContext<CatalogContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
        services.AddDbContext<InventoryContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory")));

        await using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = serviceProvider.CreateAsyncScope();

        var attempts = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .SendAsync(new PlaceOrderCommand("bolt"));

        attempts.Should().Be(2, "the handler swallowed a deadlock 40P01, then failed with 25P02 (aborted), which is classified as a conflict");

        var retryEntries = logEntries.Where(e =>
            e.Category == "Bsft.Resilience.Mediator.MediatorResiliencePipelineProvider" &&
            e.LogLevel == LogLevel.Warning &&
            e.EventId.Id == 1).ToList();

        retryEntries.Should().ContainSingle("exactly one retry warning should be logged");

        var entry = retryEntries[0];
        entry.EventId.Name.Should().Be("MediatorRequestRetrying");

        var sqlStateValue = GetStateValue(entry.State, "SqlState");
        sqlStateValue.Should().Be("40P01", "the swallowed deadlock's error code should be logged, not the 25P02 the handler failed with");

        var retryAttemptValue = GetStateValue(entry.State, "RetryAttempt");
        retryAttemptValue.Should().Be("1", "first retry is attempt 1");

        var requestTypeValue = GetStateValue(entry.State, "RequestType");
        requestTypeValue.Should().Be(typeof(PlaceOrderCommand).FullName);
    }

    private static string? GetStateValue(IReadOnlyList<System.Collections.Generic.KeyValuePair<string, object?>> state, string key)
    {
        foreach (var kv in state)
        {
            if (kv.Key == key)
                return kv.Value?.ToString();
        }

        return null;
    }

    /// <summary>A test logging provider that captures all log entries.</summary>
    private sealed class TestLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> _entries;

        public TestLoggerProvider(List<LogEntry> entries)
        {
            _entries = entries;
        }

        public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, _entries);

        public void Dispose()
        {
        }
    }

    /// <summary>A test logger that captures entries.</summary>
    private sealed class TestLogger : ILogger
    {
        private readonly string _category;
        private readonly List<LogEntry> _entries;

        public TestLogger(string category, List<LogEntry> entries)
        {
            _category = category;
            _entries = entries;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            System.Func<TState, Exception?, string> formatter)
        {
            var formattedMessage = formatter(state, exception);

            var stateList = state as IReadOnlyList<System.Collections.Generic.KeyValuePair<string, object?>> ?? [];

            _entries.Add(new LogEntry
            {
                Category = _category,
                LogLevel = logLevel,
                EventId = eventId,
                State = stateList,
                Exception = exception,
                FormattedMessage = formattedMessage,
            });
        }
    }

    /// <summary>Represents a single log entry.</summary>
    private sealed class LogEntry
    {
        public required string Category { get; init; }
        public required LogLevel LogLevel { get; init; }
        public required EventId EventId { get; init; }
        public required IReadOnlyList<System.Collections.Generic.KeyValuePair<string, object?>> State { get; init; }
        public Exception? Exception { get; init; }
        public required string FormattedMessage { get; init; }
    }
}
