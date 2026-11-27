using System.Data.Common;
using Bsft.Core.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Bsft.Resilience.Mediator;

/// <summary>
/// Builds the mediator resilience pipeline once from <see cref="BsftResilienceMediatorOptions"/>
/// and exposes it for reuse. Registered as a singleton so the (relatively expensive) pipeline
/// build happens a single time rather than per request.
/// </summary>
internal sealed partial class MediatorResiliencePipelineProvider : IMediatorResiliencePipelineProvider
{
    /// <summary>
    /// The resilience-context property <see cref="RetryBehavior{TRequest, TResponse}"/> sets, per attempt,
    /// to the <see cref="IAmbientTransaction"/> of the scope the attempt ran in, so the retry predicate can
    /// ask the store's provider whether the attempt's failure was a transaction conflict. The predicate
    /// runs after a fresh attempt's scope was disposed, which the ambient transaction answers anyway.
    /// </summary>
    internal static readonly ResiliencePropertyKey<IAmbientTransaction?> AmbientTransactionKey =
        new("Bsft.Resilience.Mediator.AmbientTransaction");

    /// <summary>
    /// The resilience-context property <see cref="RetryBehavior{TRequest, TResponse}"/> sets to the runtime
    /// type of the request it retries, so the warning logged per retry can name it.
    /// </summary>
    internal static readonly ResiliencePropertyKey<Type?> RequestTypeKey =
        new("Bsft.Resilience.Mediator.RequestType");

    /// <summary>
    /// The resilience-context property the retry predicate sets, per failed attempt, to the failure the
    /// ambient transaction classified as a conflict, so the warning logged for the retry names the error
    /// code that caused it rather than the one the handler failed with afterwards.
    /// </summary>
    internal static readonly ResiliencePropertyKey<Exception?> ConflictCauseKey =
        new("Bsft.Resilience.Mediator.ConflictCause");

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorResiliencePipelineProvider"/> class that logs
    /// nothing.
    /// </summary>
    /// <param name="options">The mediator resilience options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the options carry no retry predicate.</exception>
    public MediatorResiliencePipelineProvider(IOptions<BsftResilienceMediatorOptions> options)
        : this(options, NullLogger<MediatorResiliencePipelineProvider>.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorResiliencePipelineProvider"/> class that logs
    /// a warning per retry. The container picks it when logging is registered.
    /// </summary>
    /// <param name="options">The mediator resilience options.</param>
    /// <param name="logger">Records each retry.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the options carry no retry predicate.</exception>
    public MediatorResiliencePipelineProvider(
        IOptions<BsftResilienceMediatorOptions> options, ILogger<MediatorResiliencePipelineProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var value = options.Value;

        if (value.ShouldRetry is null)
            throw new ArgumentException($"{nameof(BsftResilienceMediatorOptions.ShouldRetry)} must not be null.", nameof(options));

        Pipeline = Build(value, logger);
    }

    /// <inheritdoc />
    public ResiliencePipeline Pipeline { get; }

    private static ResiliencePipeline Build(BsftResilienceMediatorOptions options, ILogger logger) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = options.BaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => new ValueTask<bool>(ShouldRetry(options, args.Context, args.Outcome.Exception)),
                OnRetry = args =>
                {
                    LogRetry(logger, args);
                    return default;
                },
            })
            .Build();

    /// <summary>
    /// The host's predicate, with two cases decided before it: a failure the shared transaction's
    /// provider classifies as a conflict with a concurrent transaction is retried, because the whole
    /// unit was rolled back and a fresh attempt is exactly what the store asks for; a misuse of the
    /// ambient transaction that is not such a conflict is not, because it fails the same way again.
    /// </summary>
    /// <remarks>
    /// The conflict is asked first so that a swallowed deadlock, which surfaces as the misuse of having
    /// swallowed it, is still retried: the store ended that attempt, not the caller.
    /// </remarks>
    private static bool ShouldRetry(BsftResilienceMediatorOptions options, ResilienceContext context, Exception? exception) =>
        IsTransactionConflict(context, exception)
        || (exception is not AmbientTransactionMisuseException && options.ShouldRetry(exception));

    private static bool IsTransactionConflict(ResilienceContext context, Exception? exception)
    {
        var cause = exception is not null
            && context.Properties.TryGetValue(AmbientTransactionKey, out var ambientTransaction)
            && ambientTransaction is not null
                ? ambientTransaction.FindTransactionConflict(exception)
                : null;

        context.Properties.Set(ConflictCauseKey, cause);

        return cause is not null;
    }

    /// <summary>
    /// Logs one retry as a warning: which request, which retry, the store's error code that caused it (the
    /// classified conflict's, else the first in the failure's chain) and the exception type that ended the
    /// attempt. Neither the exception nor its message is logged, because a store's message
    /// can carry the values of the rows involved.
    /// </summary>
    private static void LogRetry(ILogger logger, OnRetryArguments<object> args)
    {
        if (!logger.IsEnabled(LogLevel.Warning))
            return;

        args.Context.Properties.TryGetValue(RequestTypeKey, out var requestType);
        args.Context.Properties.TryGetValue(ConflictCauseKey, out var conflict);
        var exception = args.Outcome.Exception;

        LogRetrying(
            logger,
            requestType?.FullName ?? "unknown",
            args.AttemptNumber + 1,
            FindSqlState(conflict) ?? FindSqlState(exception) ?? "none",
            exception?.GetType().FullName ?? "none");
    }

    /// <summary>Returns the first SQLSTATE in the exception chain, or <see langword="null"/> when there is none.</summary>
    private static string? FindSqlState(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: { Length: > 0 } sqlState })
                return sqlState;
        }

        return null;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "MediatorRequestRetrying",
        Level = LogLevel.Warning,
        Message = "Retrying {RequestType} (retry {RetryAttempt}) after SQLSTATE {SqlState} ({ExceptionType}).")]
    private static partial void LogRetrying(
        ILogger logger, string requestType, int retryAttempt, string sqlState, string exceptionType);
}
