using Bsft.EventAggregator.Saga.Timeouts;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.EventAggregator.Saga.Execution;

/// <summary>
/// Default implementation of <see cref="ISagaContext{TState}"/> that wraps the saga instance,
/// event aggregator, and service provider.
/// </summary>
/// <typeparam name="TState">The type of the saga state.</typeparam>
internal sealed class SagaContext<TState> : ISagaContext<TState>
{
    private readonly IEventAggregator _eventAggregator;
    private readonly SagaInstance<TState> _instance;
    private readonly List<PendingSagaTimeout> _pendingTimeouts = [];

    /// <inheritdoc />
    public TState State => _instance.State;

    /// <inheritdoc />
    public string? CorrelationKey => _instance.CorrelationKey;

    /// <inheritdoc />
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the timeout operations recorded by the step. They are applied by the flow host once the
    /// step has finished and its state has been persisted, so a failed step leaves no timers behind.
    /// </summary>
    internal IReadOnlyList<PendingSagaTimeout> PendingTimeouts => _pendingTimeouts;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaContext{TState}"/> class.
    /// </summary>
    /// <param name="instance">The saga instance being executed.</param>
    /// <param name="eventAggregator">The event aggregator for publishing events.</param>
    /// <param name="services">The service provider for resolving dependencies.</param>
    public SagaContext(SagaInstance<TState> instance, IEventAggregator eventAggregator, IServiceProvider services)
    {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public T GetService<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        await _eventAggregator.PublishAsync(@event, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void MarkComplete()
    {
        _instance.IsComplete = true;
    }

    /// <inheritdoc />
    public void ScheduleTimeout<TEvent>(string name, TimeSpan delay, TEvent timeoutEvent, string? calendarKey = null)
    {
        Record(new PendingSagaTimeout
        {
            Kind = PendingSagaTimeoutKind.Schedule,
            Name = RequireName(name),
            Delay = delay,
            Payload = RequireEvent(timeoutEvent),
            CalendarKey = calendarKey,
            CorrelationId = CaptureCorrelationId(),
        });
    }

    /// <inheritdoc />
    public void ScheduleTimeout<TEvent>(string name, DateTimeOffset dueAt, TEvent timeoutEvent)
    {
        Record(new PendingSagaTimeout
        {
            Kind = PendingSagaTimeoutKind.Schedule,
            Name = RequireName(name),
            DueAt = dueAt,
            Payload = RequireEvent(timeoutEvent),
            CorrelationId = CaptureCorrelationId(),
        });
    }

    /// <inheritdoc />
    public void CancelTimeout(string name)
    {
        Record(new PendingSagaTimeout
        {
            Kind = PendingSagaTimeoutKind.Cancel,
            Name = RequireName(name),
        });
    }

    /// <inheritdoc />
    public void CancelAllTimeouts()
    {
        Record(new PendingSagaTimeout { Kind = PendingSagaTimeoutKind.CancelAll });
    }

    private void Record(PendingSagaTimeout operation) => _pendingTimeouts.Add(operation);

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A timeout name is required.", nameof(name));

        return name;
    }

    private static object RequireEvent<TEvent>(TEvent timeoutEvent)
    {
        if (timeoutEvent is null)
            throw new ArgumentNullException(nameof(timeoutEvent));

        return timeoutEvent;
    }

    /// <summary>
    /// Takes the correlation id here rather than in the scheduler: this is the only place that still
    /// sees the step's service scope. Falls back to the ambient id of a firing timeout so a chain of
    /// timeouts keeps the correlation of the request that started it.
    /// </summary>
    private string? CaptureCorrelationId() =>
        Services.GetService<ISagaCorrelationAccessor>()?.CorrelationId
        ?? Services.GetService<ISagaCorrelationScope>()?.Current;
}
