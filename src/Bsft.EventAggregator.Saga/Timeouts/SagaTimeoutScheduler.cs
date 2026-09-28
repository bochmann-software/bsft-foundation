using System.Text.Json;
using Bsft.Calendar;
using Bsft.EventAggregator.Saga.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Bsft.EventAggregator.Saga.Timeouts;

/// <summary>
/// Default <see cref="ISagaTimeoutScheduler"/>: resolves the business calendar, turns a delay into an
/// absolute due date and writes the result to the <see cref="ISagaTimeoutStore"/>.
/// </summary>
internal sealed class SagaTimeoutScheduler : ISagaTimeoutScheduler
{
    private readonly ISagaTimeoutStore _store;
    private readonly IBusinessCalendarProvider _calendars;
    private readonly TimeProvider _timeProvider;
    private readonly ISagaTelemetry _telemetry;
    private readonly ILogger<SagaTimeoutScheduler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaTimeoutScheduler"/> class.
    /// </summary>
    /// <param name="store">The store the timeouts are written to.</param>
    /// <param name="calendars">Resolves the business calendar a delay is measured in.</param>
    /// <param name="timeProvider">Supplies the instant a relative delay is measured from.</param>
    /// <param name="telemetry">Telemetry sink for the scheduled and cancelled counters.</param>
    /// <param name="logger">Records a failed write before it propagates.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public SagaTimeoutScheduler(
        ISagaTimeoutStore store,
        IBusinessCalendarProvider calendars,
        TimeProvider timeProvider,
        ISagaTelemetry telemetry,
        ILogger<SagaTimeoutScheduler> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _calendars = calendars ?? throw new ArgumentNullException(nameof(calendars));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task FlushAsync(
        string sagaType,
        string correlationKey,
        int stepIndex,
        IReadOnlyList<PendingSagaTimeout> operations,
        CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var operation in operations)
            {
                await ApplyAsync(sagaType, correlationKey, stepIndex, operation, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The step's state is already committed at this point, so a failure here means the saga
            // moved on without its reminders. It is rethrown, but it must not pass unrecorded: a
            // silently lost reminder is the hardest kind of bug to notice.
            // The correlation key is business data — an order, a quote, in a staffing context a
            // person — and stays out of the log, as everywhere else in the framework. The saga type,
            // the step and the timer names are enough to find the instance.
            _logger.LogError(
                ex,
                "Applying the timeouts of saga {SagaType} at step {StepIndex} failed; the reminders {TimeoutNames} were not written.",
                sagaType,
                stepIndex,
                string.Join(", ", operations.Select(Describe)));
            throw;
        }
    }

    /// <inheritdoc />
    public async Task CancelForStepAsync(string sagaType, string correlationKey, int stepIndex, CancellationToken cancellationToken = default)
    {
        await _store.CancelForStepAsync(sagaType, correlationKey, stepIndex, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CancelAllAsync(string sagaType, string correlationKey, CancellationToken cancellationToken = default)
    {
        await _store.CancelAllAsync(sagaType, correlationKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Names an operation for the log; a cancel-all carries no name of its own.</summary>
    private static string Describe(PendingSagaTimeout operation) =>
        operation.Name.Length > 0 ? operation.Name : operation.Kind.ToString();

    private async Task ApplyAsync(
        string sagaType,
        string correlationKey,
        int stepIndex,
        PendingSagaTimeout operation,
        CancellationToken cancellationToken)
    {
        switch (operation.Kind)
        {
            case PendingSagaTimeoutKind.Cancel:
                await _store.CancelAsync(sagaType, correlationKey, operation.Name, cancellationToken).ConfigureAwait(false);
                _telemetry.TimeoutCanceled(sagaType);
                break;

            case PendingSagaTimeoutKind.CancelAll:
                await _store.CancelAllAsync(sagaType, correlationKey, cancellationToken).ConfigureAwait(false);
                _telemetry.TimeoutCanceled(sagaType);
                break;

            default:
                await ScheduleAsync(sagaType, correlationKey, stepIndex, operation, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task ScheduleAsync(
        string sagaType,
        string correlationKey,
        int stepIndex,
        PendingSagaTimeout operation,
        CancellationToken cancellationToken)
    {
        var payload = operation.Payload!;
        var payloadType = payload.GetType();

        var timeout = new SagaTimeout
        {
            SagaType = sagaType,
            CorrelationKey = correlationKey,
            Name = operation.Name,
            StepIndex = stepIndex,
            DueAt = ResolveDueAt(operation),
            PayloadJson = JsonSerializer.Serialize(payload, payloadType),
            PayloadTypeName = payloadType.AssemblyQualifiedName ?? payloadType.FullName ?? payloadType.Name,
            CorrelationId = operation.CorrelationId,
            ConcurrencyToken = Guid.NewGuid(),
        };

        await _store.ScheduleAsync(timeout, cancellationToken).ConfigureAwait(false);
        _telemetry.TimeoutScheduled(sagaType);
    }

    private DateTimeOffset ResolveDueAt(PendingSagaTimeout operation)
    {
        // An absolute due date is taken as given: the caller already decided when it should fire.
        // A delay is business time and goes through the calendar.
        if (operation.DueAt is { } absolute)
            return absolute;

        var calendar = _calendars.Get(operation.CalendarKey);
        return calendar.Add(_timeProvider.GetUtcNow(), operation.Delay!.Value);
    }
}
