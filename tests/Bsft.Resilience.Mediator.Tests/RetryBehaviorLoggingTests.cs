using Bsft.Core;
using Bsft.Core.Persistence;
using Bsft.Resilience.Mediator.DependencyInjection;
using Bsft.Resilience.Mediator.Tests.Fakes;
using Bsft.Resilience.Mediator.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Resilience.Mediator.Tests;

public class RetryBehaviorLoggingTests
{
    [Retryable]
    private sealed record RetryableRequest;

    private sealed record PlainRequest;

    /// <summary>A small test subclass of System.Data.Common.DbException for testing SQL state extraction.</summary>
    private sealed class TestDbException : System.Data.Common.DbException
    {
        private readonly string _sqlState;

        public TestDbException(string sqlState, string message)
            : base(message)
        {
            _sqlState = sqlState;
        }

        public override string? SqlState => _sqlState;
    }

    private static BsftResilienceMediatorOptions FastOptions() =>
        new() { BaseDelay = TimeSpan.Zero };

    private static BsftResilienceMediatorOptions FastOptionsWithMaxRetries(int maxRetries)
    {
        var options = new BsftResilienceMediatorOptions { BaseDelay = TimeSpan.Zero };
        options.MaxRetryAttempts = maxRetries;
        return options;
    }

    private static MediatorResiliencePipelineProvider ProviderFromWithLogger(
        BsftResilienceMediatorOptions options, ILogger<MediatorResiliencePipelineProvider> logger) =>
        new(Options.Create(options), logger);

    [Fact]
    public async Task HandleAsync_WhenTheRequestIsRetriedTwice_LogsOneWarningPerRetry()
    {
        var options = FastOptionsWithMaxRetries(2);
        var logger = new ListLogger<MediatorResiliencePipelineProvider>();
        var behavior = new RetryBehavior<RetryableRequest, string>(ProviderFromWithLogger(options, logger));
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            if (calls <= options.MaxRetryAttempts)
                throw new InvalidOperationException("transient");
            return Task.FromResult("ok");
        }

        var result = await behavior.HandleAsync(new RetryableRequest(), Next);

        result.Should().Be("ok");
        calls.Should().Be(options.MaxRetryAttempts + 1);

        logger.Entries.Should().HaveCount(2, "exactly 2 retries should be logged");

        for (int i = 0; i < 2; i++)
        {
            var entry = logger.Entries[i];
            entry.LogLevel.Should().Be(LogLevel.Warning);
            entry.EventId.Id.Should().Be(1);
            entry.EventId.Name.Should().Be("MediatorRequestRetrying");

            var state = entry.State;
            var retryAttempt = GetStateValue(state, "RetryAttempt");
            var requestType = GetStateValue(state, "RequestType");

            requestType.Should().Be(typeof(RetryableRequest).FullName);
            retryAttempt.Should().Be((i + 1).ToString());
        }
    }

    [Fact]
    public async Task HandleAsync_WhenTheFailureCarriesASqlState_LogsItWithoutTheMessage()
    {
        var options = FastOptionsWithMaxRetries(1);
        var logger = new ListLogger<MediatorResiliencePipelineProvider>();
        var behavior = new RetryBehavior<RetryableRequest, string>(ProviderFromWithLogger(options, logger));
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            if (calls == 1)
            {
                var dbEx = new TestDbException("40P01", "secret-value-123");
                throw new InvalidOperationException("wrapper", dbEx);
            }

            return Task.FromResult("ok");
        }

        var result = await behavior.HandleAsync(new RetryableRequest(), Next);

        result.Should().Be("ok");

        logger.Entries.Should().HaveCount(1);
        var entry = logger.Entries[0];

        var state = entry.State;
        var sqlState = GetStateValue(state, "SqlState");
        var exceptionType = GetStateValue(state, "ExceptionType");

        sqlState.Should().Be("40P01");
        exceptionType.Should().Be(typeof(InvalidOperationException).FullName);

        entry.Exception.Should().BeNull("the exception object should not be logged");
        entry.FormattedMessage.Should().NotContain("secret-value-123");

        var stateDict = state.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
        var allStateValues = string.Join("|", stateDict.Values);
        allStateValues.Should().NotContain("secret-value-123");
    }

    [Fact]
    public async Task HandleAsync_WhenTheFirstAttemptSucceeds_LogsNothing()
    {
        var options = FastOptions();
        var logger = new ListLogger<MediatorResiliencePipelineProvider>();
        var behavior = new RetryBehavior<RetryableRequest, string>(ProviderFromWithLogger(options, logger));
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            return Task.FromResult("ok");
        }

        var result = await behavior.HandleAsync(new RetryableRequest(), Next);

        result.Should().Be("ok");
        calls.Should().Be(1);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenTheFailureIsNotRetried_LogsNothing()
    {
        var options = new BsftResilienceMediatorOptions { BaseDelay = TimeSpan.Zero, ShouldRetry = _ => false };
        var logger = new ListLogger<MediatorResiliencePipelineProvider>();
        var behavior = new RetryBehavior<RetryableRequest, string>(ProviderFromWithLogger(options, logger));
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            throw new InvalidOperationException("transient");
        }

        var act = () => behavior.HandleAsync(new RetryableRequest(), Next);

        await act.Should().ThrowAsync<InvalidOperationException>();
        calls.Should().Be(1);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenTheFailureHasNoSqlState_LogsSqlStateNone()
    {
        var options = FastOptionsWithMaxRetries(1);
        var logger = new ListLogger<MediatorResiliencePipelineProvider>();
        var behavior = new RetryBehavior<RetryableRequest, string>(ProviderFromWithLogger(options, logger));
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            if (calls == 1)
                throw new InvalidOperationException("no db exception");
            return Task.FromResult("ok");
        }

        var result = await behavior.HandleAsync(new RetryableRequest(), Next);

        result.Should().Be("ok");

        logger.Entries.Should().HaveCount(1);
        var entry = logger.Entries[0];

        var state = entry.State;
        var sqlState = GetStateValue(state, "SqlState");

        sqlState.Should().Be("none");
    }

    [Fact]
    public async Task Constructor_WithOldSignatureOnly_LogsNothing()
    {
        var options = FastOptionsWithMaxRetries(1);
        var logger = new ListLogger<MediatorResiliencePipelineProvider>();
        var behavior = new RetryBehavior<RetryableRequest, string>(
            new MediatorResiliencePipelineProvider(Options.Create(options)));
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            if (calls == 1)
                throw new InvalidOperationException("transient");
            return Task.FromResult("ok");
        }

        var result = await behavior.HandleAsync(new RetryableRequest(), Next);

        result.Should().Be("ok");
        calls.Should().Be(options.MaxRetryAttempts + 1);
        logger.Entries.Should().BeEmpty("the old constructor should not log anything");
    }

    [Fact]
    public async Task AddBsftResilienceMediator_WithLoggingRegistered_UsesTheLoggingConstructor()
    {
        var logger = new ListLogger<MediatorResiliencePipelineProvider>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILogger<MediatorResiliencePipelineProvider>>(logger);
        services.AddBsftResilienceMediator(options => options.BaseDelay = TimeSpan.Zero);

        using var provider = services.BuildServiceProvider();
        var behavior = new RetryBehavior<RetryableRequest, string>(
            provider.GetRequiredService<IMediatorResiliencePipelineProvider>());
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            if (calls == 1)
                throw new InvalidOperationException("transient");
            return Task.FromResult("ok");
        }

        var result = await behavior.HandleAsync(new RetryableRequest(), Next);

        result.Should().Be("ok");
        logger.Entries.Should().ContainSingle("the container built the provider with the registered logger");
    }

    [Fact]
    public void AddBsftResilienceMediator_WithoutLoggingRegistered_StillResolves()
    {
        var services = new ServiceCollection();
        services.AddBsftResilienceMediator(options => options.BaseDelay = TimeSpan.Zero);

        using var provider = services.BuildServiceProvider();

        var resilienceProvider = provider.GetRequiredService<IMediatorResiliencePipelineProvider>();
        resilienceProvider.Should().NotBeNull();
    }

    [Fact]
    public async Task Pipeline_WhenRunDirectlyWithoutARequestType_LogsUnknown()
    {
        var options = new BsftResilienceMediatorOptions { BaseDelay = TimeSpan.Zero, MaxRetryAttempts = 1 };
        var logger = new ListLogger<MediatorResiliencePipelineProvider>();
        var provider = ProviderFromWithLogger(options, logger);
        var calls = 0;

        async ValueTask<string> Handler(Polly.ResilienceContext ctx)
        {
            calls++;
            if (calls == 1)
                throw new InvalidOperationException("transient");
            return await Task.FromResult("ok");
        }

        var context = Polly.ResilienceContextPool.Shared.Get(CancellationToken.None);
        try
        {
            var result = await provider.Pipeline.ExecuteAsync(Handler, context);

            result.Should().Be("ok");
            calls.Should().Be(2);

            logger.Entries.Should().ContainSingle("one retry should be logged");
            var entry = logger.Entries[0];

            var requestType = GetStateValue(entry.State, "RequestType");
            requestType.Should().Be("unknown", "no RequestTypeKey was set in the context");
        }
        finally
        {
            Polly.ResilienceContextPool.Shared.Return(context);
        }
    }

    [Fact]
    public async Task HandleAsync_WhenTheHostRetriesASuccessfulResult_LogsExceptionTypeNone()
    {
        var options = new BsftResilienceMediatorOptions
        {
            BaseDelay = TimeSpan.Zero,
            MaxRetryAttempts = 1,
            ShouldRetry = _ => _ is null
        };
        var logger = new ListLogger<MediatorResiliencePipelineProvider>();
        var behavior = new RetryBehavior<RetryableRequest, string>(ProviderFromWithLogger(options, logger));

        Task<string> Next()
        {
            return Task.FromResult("ok");
        }

        var result = await behavior.HandleAsync(new RetryableRequest(), Next);

        result.Should().Be("ok");

        logger.Entries.Should().ContainSingle("one retry logged when retrying a successful result");
        var entry = logger.Entries[0];

        var exceptionType = GetStateValue(entry.State, "ExceptionType");
        var sqlState = GetStateValue(entry.State, "SqlState");

        exceptionType.Should().Be("none", "a successful result has no exception");
        sqlState.Should().Be("none", "a successful result has no DbException");
    }

    [Fact]
    public async Task HandleAsync_WhenTheConflictIsARecordedFailure_LogsItsSqlState()
    {
        var options = FastOptionsWithMaxRetries(1);
        var logger = new ListLogger<MediatorResiliencePipelineProvider>();
        var fakeAmbientTransaction = new RecordedConflictAmbientTransaction(new TestDbException("40P01", "original deadlock"));

        var calls = 0;

        Task<string> Next()
        {
            calls++;
            if (calls == 1)
                throw new InvalidOperationException("handler fails with 25P02", new TestDbException("25P02", "aborted"));
            return Task.FromResult("ok");
        }

        var context = Polly.ResilienceContextPool.Shared.Get(CancellationToken.None);
        context.Properties.Set(MediatorResiliencePipelineProvider.AmbientTransactionKey, fakeAmbientTransaction);
        context.Properties.Set(MediatorResiliencePipelineProvider.RequestTypeKey, typeof(RetryableRequest));

        try
        {
            var result = await new MediatorResiliencePipelineProvider(Options.Create(options), logger)
                .Pipeline.ExecuteAsync(
                    static (_, state) => new ValueTask<string>(state()),
                    context,
                    Next);

            result.Should().Be("ok");
            logger.Entries.Should().ContainSingle("one retry logged");

            var entry = logger.Entries[0];
            var sqlState = GetStateValue(entry.State, "SqlState");

            sqlState.Should().Be("40P01", "the conflict's SqlState should be logged, not the exception's");
        }
        finally
        {
            Polly.ResilienceContextPool.Shared.Return(context);
        }
    }

    [Fact]
    public async Task HandleAsync_WhenTheConflictIsTheFailureItself_LogsTheFailuresSqlState()
    {
        var options = FastOptionsWithMaxRetries(1);
        var logger = new ListLogger<MediatorResiliencePipelineProvider>();

        // No override of FindTransactionConflict: the interface's default answers with the failure itself.
        IAmbientTransaction ambientTransaction = new FakeAmbientTransaction { ConflictType = typeof(InvalidOperationException) };
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            if (calls == 1)
                throw new InvalidOperationException("handler fails with 25P02", new TestDbException("25P02", "aborted"));
            return Task.FromResult("ok");
        }

        var context = Polly.ResilienceContextPool.Shared.Get(CancellationToken.None);
        context.Properties.Set(MediatorResiliencePipelineProvider.AmbientTransactionKey, ambientTransaction);

        try
        {
            var result = await new MediatorResiliencePipelineProvider(Options.Create(options), logger)
                .Pipeline.ExecuteAsync(
                    static (_, state) => new ValueTask<string>(state()),
                    context,
                    Next);

            result.Should().Be("ok");
            logger.Entries.Should().ContainSingle();
            GetStateValue(logger.Entries[0].State, "SqlState").Should().Be("25P02");
        }
        finally
        {
            Polly.ResilienceContextPool.Shared.Return(context);
        }
    }

    [Fact]
    public void FindTransactionConflict_ByDefault_AnswersWithTheFailureWhenItIsAConflict()
    {
        IAmbientTransaction ambientTransaction = new FakeAmbientTransaction { ConflictType = typeof(TimeoutException) };
        var conflict = new TimeoutException();

        ambientTransaction.FindTransactionConflict(conflict).Should().BeSameAs(conflict);
        ambientTransaction.FindTransactionConflict(new InvalidOperationException()).Should().BeNull();
    }

    /// <summary>An ambient transaction that reports a recorded failure as the conflict behind every failure.</summary>
    private sealed class RecordedConflictAmbientTransaction(Exception conflict) : IAmbientTransaction
    {
        public bool IsActive => false;

        public bool IsTransactionConflict(Exception exception) => true;

        public Exception? FindTransactionConflict(Exception exception) => conflict;

        public Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default) =>
            work(cancellationToken);
    }

    /// <summary>Extracts a value from structured logging state by key.</summary>
    private static string? GetStateValue(IReadOnlyList<KeyValuePair<string, object?>> state, string key)
    {
        foreach (var kv in state)
        {
            if (kv.Key == key)
                return kv.Value?.ToString();
        }

        return null;
    }
}
