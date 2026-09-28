using Microsoft.Extensions.Logging;

namespace Bsft.Outbox;

/// <summary>
/// One drain pass over one outbox: lease a batch, apply each row's side effect, mark what succeeded,
/// and hand back or retire what did not.
/// </summary>
/// <typeparam name="TPayload">What the rows of this outbox carry.</typeparam>
/// <remarks>
/// <para>
/// Application is at-least-once, and the order of the two steps is what buys it. The row was written
/// in the same transaction as the state change it describes, so it exists whenever that change
/// exists; it is marked processed only after the side effect has been applied, so a crash anywhere in
/// between leaves it pending and the effect is applied again. What a crash can cost is a duplicate,
/// never a lost row, which is why <see cref="IOutboxSideEffect{TPayload}"/> implementations must be
/// idempotent.
/// </para>
/// <para>
/// Several application instances may drain the same outbox. A row is leased before its effect is
/// applied, and only the lease holder can complete it, so the instances share the backlog instead of
/// each working all of it.
/// </para>
/// <para>
/// The loop around this pass is not here. <see cref="Core.Polling.PollLoop"/> already owns that shape,
/// and a hosted service phrases what it has to say about its own passes in its own words.
/// </para>
/// </remarks>
public sealed class OutboxDrainer<TPayload> : IOutboxDrainer<TPayload>
{
    private readonly IOutboxStore<TPayload> _store;
    private readonly IOutboxSideEffect<TPayload> _sideEffect;
    private readonly IOutboxRetrySchedule _retrySchedule;
    private readonly BsftOutboxOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of the <see cref="OutboxDrainer{TPayload}"/> class.</summary>
    /// <param name="store">The outbox store to drain.</param>
    /// <param name="sideEffect">What a row means, applied before the row is marked.</param>
    /// <param name="retrySchedule">Decides how long a failed row is held back.</param>
    /// <param name="options">The bounds of a pass.</param>
    /// <param name="timeProvider">Time provider used to lease and to stamp the processed instant.</param>
    /// <param name="logger">Logger used to report failures and retirements.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the batch size is below one, the poll interval is negative, the lease duration is
    /// not positive, the retry backoff is negative or the retry ceiling is not positive.
    /// </exception>
    public OutboxDrainer(
        IOutboxStore<TPayload> store,
        IOutboxSideEffect<TPayload> sideEffect,
        IOutboxRetrySchedule retrySchedule,
        BsftOutboxOptions options,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sideEffect = sideEffect ?? throw new ArgumentNullException(nameof(sideEffect));
        _retrySchedule = retrySchedule ?? throw new ArgumentNullException(nameof(retrySchedule));
        _options = OutboxDrainBounds.EnsureWorkable(options ?? throw new ArgumentNullException(nameof(options)));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<int> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var leased = await _store
            .LeaseAsync(now, _options.LeaseDuration, _options.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        // Every row of the batch is leased until the same instant — the contract on
        // IOutboxStore.LeaseAsync fixes it at now plus the lease duration — while the side effects are
        // applied sequentially. The tail therefore spends whatever deadline the rows before it did not
        // use.
        var leaseExpiresAt = now + _options.LeaseDuration;

        var attempted = 0;
        var applied = 0;

        foreach (var row in leased)
        {
            // Past that instant the lease no longer belongs to this drainer. Applying anyway produces
            // an effect that can no longer be marked, so the next holder applies it a second time;
            // stopping instead leaves the rest of the batch pending under an expired lease, which is
            // exactly what the next pass claims. Nothing is lost either way, this merely stops paying
            // for a duplicate that buys nothing. The first row is always attempted, so a pass still
            // makes progress when the batch was leased more slowly than the lease lives.
            if (attempted > 0 && _timeProvider.GetUtcNow() >= leaseExpiresAt)
            {
                _logger.LogWarning(
                    "The outbox batch lease expired after {AttemptedCount} of {LeasedCount} rows; the rest stay pending for the next pass. Lower BatchSize or raise LeaseDuration so a batch fits into one lease.",
                    attempted,
                    leased.Count);

                break;
            }

            attempted++;

            if (await TryApplyAsync(row, cancellationToken).ConfigureAwait(false))
                applied++;
        }

        return applied;
    }

    private async Task<bool> TryApplyAsync(LeasedOutboxRow<TPayload> row, CancellationToken cancellationToken)
    {
        var application = ApplyAsync(row, cancellationToken);

        // A row whose side effect failed has to go back into the outbox rather than end the pass, so
        // the outcome is observed instead of caught.
        await Task.WhenAny(application).ConfigureAwait(false);

        var failure = FailureOf(application, row, cancellationToken);

        if (failure is not null)
        {
            await RecordFailureAsync(row, failure, cancellationToken).ConfigureAwait(false);
            return false;
        }

        await application.ConfigureAwait(false);

        var marked = await _store
            .TryMarkProcessedAsync(row.Id, row.LeaseToken, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        if (!marked)
        {
            // The lease expired while the effect was in flight and another instance owns the row now.
            // The effect above stands, so this row is one of the duplicates at-least-once permits.
            _logger.LogWarning(
                "Outbox row {OutboxRowId} ({OutboxPayload}) was applied but its lease had already expired; it may be applied again.",
                row.Id,
                _sideEffect.Describe(row.Payload));
        }

        return marked;
    }

    /// <summary>
    /// Reads the outcome of a finished side effect: the failure the row has to be tried again for, or
    /// <see langword="null"/> when it was applied or the caller itself is stopping.
    /// </summary>
    /// <remarks>
    /// A side effect that ends cancelled while nobody asked the drainer to stop was cancelled by a
    /// token of its own — an HTTP client timeout is the usual one. Such a cancellation is a failed
    /// attempt: taking it for a shutdown would let it unwind through the pass and end the loop above,
    /// so a single handler timeout would stop the outbox draining for as long as the host lives.
    /// </remarks>
    private AggregateException? FailureOf(Task application, LeasedOutboxRow<TPayload> row, CancellationToken cancellationToken)
    {
        if (application.IsFaulted)
            return application.Exception!.Flatten();

        if (application.IsCanceled && !cancellationToken.IsCancellationRequested)
        {
            return new AggregateException(new TaskCanceledException(
                $"Applying the outbox row was canceled by a token of its own rather than by a shutdown of the drainer; {_sideEffect.Describe(row.Payload)} was not applied."));
        }

        return null;
    }

    /// <summary>
    /// Puts a failed row back for another attempt — held back until the retry schedule allows it — or
    /// retires it once it has used up its attempts. Retiring keeps the row and the reason it failed; a
    /// row whose effect can never succeed would otherwise be leased, logged and released for as long
    /// as the application runs.
    /// </summary>
    private async Task RecordFailureAsync(
        LeasedOutboxRow<TPayload> row, AggregateException failure, CancellationToken cancellationToken)
    {
        var attempt = row.AttemptCount + 1;
        var described = _sideEffect.Describe(row.Payload);

        if (attempt >= _options.MaxDeliveryAttempts)
        {
            // A single failure is recorded by its own message: the aggregate's text only says that
            // something went wrong, which is exactly what the recorded reason must not say.
            var reason = failure.InnerExceptions.Count == 1 ? failure.InnerExceptions[0].Message : failure.Message;

            _logger.LogError(
                failure,
                "Outbox row {OutboxRowId} ({OutboxPayload}) failed {AttemptNumber} times and is retired to the dead-letter state; it will not be applied without intervention.",
                row.Id,
                described,
                attempt);

            await _store
                .DeadLetterAsync(row.Id, row.LeaseToken, _timeProvider.GetUtcNow(), reason, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        // The wait is computed here rather than in the store because this is where both halves of it
        // are known: the attempt number the schedule doubles on, and the failure that may have asked
        // for a wait of its own. The store only records the instant it produced.
        var delay = _retrySchedule.NextDelay(
            attempt, _options.RetryBackoff, _options.MaxRetryDelay, _retrySchedule.RequestedRetryAfter(failure));

        var nextAttemptAt = _timeProvider.GetUtcNow() + delay;

        _logger.LogError(
            failure,
            "Outbox row {OutboxRowId} ({OutboxPayload}) failed on attempt {AttemptNumber}; it is held back for {RetryDelay} and retried at {NextAttemptAt}.",
            row.Id,
            described,
            attempt,
            delay,
            nextAttemptAt);

        await _store.AbandonAsync(row.Id, row.LeaseToken, nextAttemptAt, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Hands the payload to the side effect. Awaiting rather than returning its task is what keeps a
    /// side effect that validates its argument before its first await — and so throws synchronously —
    /// inside the failure handling above instead of unwinding the whole pass.
    /// </summary>
    private async Task ApplyAsync(LeasedOutboxRow<TPayload> row, CancellationToken cancellationToken) =>
        await _sideEffect.ApplyAsync(row.Payload, cancellationToken).ConfigureAwait(false);
}
