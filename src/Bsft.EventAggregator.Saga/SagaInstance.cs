namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Represents a running instance of a saga, holding its state and progress information.
/// </summary>
/// <typeparam name="TState">The type of the saga state.</typeparam>
public sealed class SagaInstance<TState>
{
    /// <summary>
    /// Gets the type name that identifies this saga definition.
    /// </summary>
    public string SagaType { get; }

    /// <summary>
    /// Gets the correlation key that uniquely identifies this saga instance within its type.
    /// </summary>
    public string? CorrelationKey { get; }

    /// <summary>
    /// Gets or sets the current state of the saga.
    /// </summary>
    public TState State { get; set; }

    /// <summary>
    /// Gets or sets the index of the current step in the saga definition.
    /// </summary>
    public int CurrentStepIndex { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this saga instance has completed.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Gets or sets the optimistic-concurrency token captured when this instance was loaded from a
    /// store, or <see cref="Guid.Empty"/> for an instance that has never been persisted. A store
    /// compares this load-time value against the row it is about to write and refuses a save built on
    /// a snapshot a concurrent writer has since advanced, rather than silently overwriting it. The
    /// value is opaque to the saga engine; only the store that produced it interprets it, and
    /// <see cref="Guid.Empty"/> is the unset sentinel exactly as <c>Subscription.Version</c> and
    /// <c>FeatureFlag.Version</c> use it.
    /// </summary>
    public Guid Version { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaInstance{TState}"/> class.
    /// </summary>
    /// <param name="sagaType">The type name that identifies the saga definition.</param>
    /// <param name="correlationKey">The correlation key for this instance.</param>
    /// <param name="state">The initial state.</param>
    public SagaInstance(string sagaType, string? correlationKey, TState state)
    {
        SagaType = sagaType ?? throw new ArgumentNullException(nameof(sagaType));
        CorrelationKey = correlationKey;
        State = state;
    }
}
