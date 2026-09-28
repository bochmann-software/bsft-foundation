namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Holds the complete definition of a saga, including its steps, type name, and whether it auto-completes.
/// </summary>
/// <typeparam name="TState">The type of the saga state.</typeparam>
public sealed class SagaDefinition<TState>
{
    /// <summary>
    /// Gets the unique type name identifying this saga definition.
    /// </summary>
    public string SagaType { get; }

    /// <summary>
    /// Gets the ordered list of steps that make up this saga.
    /// </summary>
    public IReadOnlyList<SagaStep> Steps { get; }

    /// <summary>
    /// Gets a value indicating whether the saga automatically completes after the last step executes.
    /// </summary>
    public bool AutoComplete { get; }

    /// <summary>
    /// Gets the event type that triggers the first step of this saga.
    /// </summary>
    public Type InitialEventType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaDefinition{TState}"/> class.
    /// </summary>
    /// <param name="sagaType">The unique type name for this saga.</param>
    /// <param name="steps">The ordered list of saga steps.</param>
    /// <param name="autoComplete">Whether the saga auto-completes after the last step.</param>
    /// <param name="initialEventType">The event type that triggers the first step.</param>
    public SagaDefinition(string sagaType, IReadOnlyList<SagaStep> steps, bool autoComplete, Type initialEventType)
    {
        SagaType = sagaType ?? throw new ArgumentNullException(nameof(sagaType));
        Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        InitialEventType = initialEventType ?? throw new ArgumentNullException(nameof(initialEventType));
        AutoComplete = autoComplete;
    }
}
