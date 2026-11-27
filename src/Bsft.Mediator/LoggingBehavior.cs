using Microsoft.Extensions.Logging;

namespace Bsft.Mediator;

/// <summary>
/// Pipeline behavior that logs the start, duration, and outcome of command and query execution.
/// When an <see cref="ICorrelationIdAccessor"/> is registered, includes the correlation ID in log entries.
/// </summary>
/// <typeparam name="TRequest">The type of the request.</typeparam>
/// <typeparam name="TResponse">The type of the response.</typeparam>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ICorrelationIdAccessor? _correlationIdAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="timeProvider">The clock the logged duration is measured on.</param>
    /// <param name="correlationIdAccessor">An optional accessor providing the current correlation ID.</param>
    public LoggingBehavior(
        ILogger<LoggingBehavior<TRequest, TResponse>> logger,
        TimeProvider timeProvider,
        ICorrelationIdAccessor? correlationIdAccessor = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _correlationIdAccessor = correlationIdAccessor;
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default)
    {
        // The mediator dispatches under the declared contract, so the closed generic would name
        // every command of one result type alike; the log needs the request the caller sent.
        var requestName = request?.GetType().Name ?? typeof(TRequest).Name;
        var correlationId = CorrelationIds.Safe(_correlationIdAccessor?.CorrelationId);

        _logger.LogInformation(
            "[{CorrelationId}] Handling {RequestName}",
            correlationId,
            requestName);

        var started = _timeProvider.GetTimestamp();

        try
        {
            var response = await next().ConfigureAwait(false);

            _logger.LogInformation(
                "[{CorrelationId}] Handled {RequestName} in {ElapsedMilliseconds}ms",
                correlationId,
                requestName,
                ElapsedMillisecondsSince(started));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[{CorrelationId}] Failed {RequestName} after {ElapsedMilliseconds}ms",
                correlationId,
                requestName,
                ElapsedMillisecondsSince(started));

            throw;
        }
    }

    /// <summary>The whole milliseconds the clock has moved since <paramref name="started"/>.</summary>
    /// <param name="started">A timestamp taken from the same clock.</param>
    /// <returns>The elapsed time in whole milliseconds, as the log has always written it.</returns>
    private long ElapsedMillisecondsSince(long started) =>
        (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds;
}
