using Bsft.EventAggregator.Saga.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.EventAggregator.Saga.Timeouts;

/// <summary>
/// Background service that fires due saga timeouts: it polls the <see cref="ISagaTimeoutStore"/>,
/// applies the configured catch-up policy and publishes the carried event through the event
/// aggregator, where the ordinary saga step matching takes over.
/// </summary>
/// <remarks>
/// <para>
/// The dispatcher never executes business logic. It publishes an event and nothing else, which keeps
/// the flow <c>Event → Saga Step → Command → Handler → Event</c> intact.
/// </para>
/// <para>
/// Delivery is at-least-once. Two application instances can both publish the same due timeout before
/// either marks it dispatched; the loser's mark fails on the concurrency token. A second delivery is
/// harmless for the saga itself — the step guard in <see cref="Bsft.EventAggregator.Saga.Execution.SagaFlowHost"/> drops an event whose
/// step has already advanced — but any side effect triggered by a timeout event must be idempotent.
/// </para>
/// </remarks>
public sealed class SagaTimeoutDispatcher : BackgroundService, ISagaTimeoutDrainer
{
    private readonly ISagaTimeoutStore _store;
    private readonly ISagaTimeoutPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly SagaTimeoutOptions _options;
    private readonly ISagaTelemetry _telemetry;
    private readonly ILogger<SagaTimeoutDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaTimeoutDispatcher"/> class.
    /// </summary>
    /// <param name="store">The timeout store to drain.</param>
    /// <param name="publisher">Publishes the event carried by a due timeout.</param>
    /// <param name="timeProvider">Time provider used to determine due timeouts and stamp dispatches.</param>
    /// <param name="options">Dispatcher options.</param>
    /// <param name="telemetry">Telemetry sink for timeout counters.</param>
    /// <param name="logger">Logger used to report dispatch failures.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the batch size is below one, or the poll interval or maximum catch-up age is negative.
    /// </exception>
    public SagaTimeoutDispatcher(
        ISagaTimeoutStore store,
        ISagaTimeoutPublisher publisher,
        TimeProvider timeProvider,
        IOptions<BsftSagaOptions> options,
        ISagaTelemetry telemetry,
        ILogger<SagaTimeoutDispatcher> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = Validate(options?.Value.Timeouts ?? throw new ArgumentNullException(nameof(options)));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Rejects options that would break the loop silently or loudly. A non-positive batch size makes
    /// every poll return nothing, so timers would simply never fire; a negative poll interval makes
    /// the delay throw and takes the host down with it; a catch-up policy that names nothing would have
    /// a missed timeout answered by whichever branch the selection falls through to.
    /// </summary>
    private static SagaTimeoutOptions Validate(SagaTimeoutOptions options)
    {
        if (options.BatchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(SagaTimeoutOptions.BatchSize), options.BatchSize, "The batch size must be at least 1.");
        if (options.PollInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SagaTimeoutOptions.PollInterval), options.PollInterval, "The poll interval must not be negative.");
        if (options.MaxCatchUpAge < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SagaTimeoutOptions.MaxCatchUpAge), options.MaxCatchUpAge, "The maximum catch-up age must not be negative.");
        if (options.MaxDispatchAttempts < 1)
            throw new ArgumentOutOfRangeException(
                nameof(SagaTimeoutOptions.MaxDispatchAttempts),
                options.MaxDispatchAttempts,
                "The maximum number of dispatch attempts must be at least 1.");
        if (!Enum.IsDefined(options.CatchUpPolicy))
            throw new ArgumentOutOfRangeException(
                nameof(SagaTimeoutOptions.CatchUpPolicy),
                options.CatchUpPolicy,
                "The catch-up policy must be one that this dispatcher knows.");

        return options;
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunLoopAsync(stoppingToken);

    /// <summary>Runs the polling loop until the stopping token is cancelled.</summary>
    /// <param name="stoppingToken">The token that stops the loop when cancelled.</param>
    /// <returns>A task that completes when the loop has unwound.</returns>
    internal async Task RunLoopAsync(CancellationToken stoppingToken = default)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                int processed;
                try
                {
                    processed = await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // A pass that ends cancelled while nobody asked the dispatcher to stop was cancelled
                    // by a token of its own — a store call that timed out, typically. Reading that as
                    // shutdown would leave the host running with no saga timer ever firing again, so it
                    // is treated as a failed pass and the next poll tries again.
                    _logger.LogError("Saga timeout dispatch pass was canceled although the dispatcher was not asked to stop.");
                    processed = 0;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Keep the long-running service alive on transient failures; back off and retry.
                    _logger.LogError(ex, "Saga timeout dispatch pass failed.");
                    processed = 0;
                }

                if (processed == 0)
                    await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected when the stopping token is cancelled during a poll or delay on shutdown. The
            // filter is what keeps that meaning: without it any cancellation from anywhere below would
            // end the loop as a shutdown, and one handler timeout would stop saga timers for good while
            // the host kept running. A cancellation with no stop behind it leaves the loop loudly.
            _logger.LogDebug("Saga timeout dispatcher stopping.");
        }
    }

    /// <inheritdoc />
    public Task<int> DrainDueAsync(CancellationToken cancellationToken = default) => ProcessOnceAsync(cancellationToken);

    /// <summary>Processes one batch of due timeouts and returns how many were handled.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The number of timeouts fired or skipped in this pass.</returns>
    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var due = await _store.ListDueAsync(now, _options.BatchSize, cancellationToken).ConfigureAwait(false);
        if (due.Count == 0)
            return 0;

        var skipped = SelectSkipped(due, now);
        var handled = 0;

        if (skipped.Count == 0)
        {
            foreach (var timeout in due)
            {
                if (await TryFireAsync(timeout, now, cancellationToken).ConfigureAwait(false))
                    handled++;
            }

            return handled;
        }

        // Discarded timeouts are retired in one statement rather than one per row: a catch-up policy
        // can drop a whole batch after an outage.
        await _store.SkipAsync(skipped, now, cancellationToken).ConfigureAwait(false);

        foreach (var timeout in due)
        {
            if (skipped.Contains(timeout.Id))
            {
                _telemetry.TimeoutMissed(timeout.SagaType);
                handled++;
            }
            else if (await TryFireAsync(timeout, now, cancellationToken).ConfigureAwait(false))
            {
                handled++;
            }
        }

        return handled;
    }

    /// <summary>
    /// Determines which of the due timeouts the catch-up policy discards instead of firing.
    /// </summary>
    private HashSet<long> SelectSkipped(IReadOnlyList<SagaTimeout> due, DateTimeOffset now)
    {
        switch (_options.CatchUpPolicy)
        {
            case SagaTimeoutCatchUpPolicy.SkipBeyondMaxAge:
                return [.. due.Where(t => now - t.DueAt > _options.MaxCatchUpAge).Select(t => t.Id)];

            case SagaTimeoutCatchUpPolicy.FireLatestPerSaga:
                // Keep the newest timeout per saga instance and discard the backlog behind it, so a
                // long outage does not replay a burst of stale reminders at one saga.
                return
                [
                    .. due
                        .GroupBy(t => (t.SagaType, t.CorrelationKey))
                        .SelectMany(g => g.OrderByDescending(t => t.DueAt).ThenByDescending(t => t.Id).Skip(1))
                        .Select(t => t.Id),
                ];

            default:
                return [];
        }
    }

    private async Task<bool> TryFireAsync(SagaTimeout timeout, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await _store
                .TryDispatchAsync(timeout, now, PublishAsync, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelled by a token of its own rather than by a shutdown — an HttpClient timeout inside
            // the woken step is the usual one. Letting it unwind would end the polling loop, so a single
            // step timeout would stop every saga timer for as long as the host lives. The timeout is
            // identifiable here, so the attempt is counted against it like any other failure.
            await RecordFailureAsync(timeout, ex, now, cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordFailureAsync(timeout, ex, now, cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    /// <summary>
    /// Leaves a failed timeout pending for another attempt, or retires it once it has used up its
    /// attempts. Retiring keeps the row and the reason it failed; a timeout that can never fire would
    /// otherwise hold its place at the head of every poll, and a batch of them stops the timers of the
    /// whole application rather than of the one saga they belong to.
    /// </summary>
    private async Task RecordFailureAsync(SagaTimeout timeout, Exception failure, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var attempt = timeout.AttemptCount + 1;

        if (attempt >= _options.MaxDispatchAttempts)
        {
            _logger.LogError(
                failure,
                "Saga timeout {TimeoutId} ({TimeoutName}) failed {AttemptNumber} times and is retired to the dead-letter state; it will not fire without intervention.",
                timeout.Id,
                timeout.Name,
                attempt);

            await _store.DeadLetterAsync(timeout.Id, now, failure.Message, cancellationToken).ConfigureAwait(false);
            _telemetry.TimeoutDeadLettered(timeout.SagaType);
            return;
        }

        _logger.LogError(
            failure,
            "Saga timeout {TimeoutId} ({TimeoutName}) failed to fire on attempt {AttemptNumber}; it stays pending for a retry.",
            timeout.Id,
            timeout.Name,
            attempt);

        await _store.RecordFailedAttemptAsync(timeout.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishAsync(SagaTimeout timeout, CancellationToken cancellationToken)
    {
        // The span is opened here rather than inside the publisher so that it wraps the correlation
        // scope the publisher enters, not the other way round: the dispatch span is the parent of
        // everything the woken saga step goes on to do under the original correlation id.
        using (_telemetry.BeginTimeoutDispatch(timeout.SagaType, timeout.Name))
        {
            await _publisher.PublishAsync(timeout, cancellationToken).ConfigureAwait(false);
        }

        _telemetry.TimeoutFired(timeout.SagaType);
    }
}
