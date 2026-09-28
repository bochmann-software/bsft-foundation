namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Provides a fluent API for defining a saga with a specific state type, including its steps, event triggers, and completion behavior.
/// </summary>
/// <typeparam name="TState">The type of the saga state.</typeparam>
public sealed class SagaBuilder<TState> where TState : new()
{
    private readonly ISagaEngine _engine;
    private readonly ISagaDefinitionRegistry _registry;
    private readonly List<SagaStep> _steps = [];
    private Type? _initialEventType;
    private string? _sagaType;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaBuilder{TState}"/> class.
    /// </summary>
    /// <param name="engine">The saga engine used to start saga instances.</param>
    /// <param name="registry">The registry that tracks built definitions.</param>
    internal SagaBuilder(ISagaEngine engine, ISagaDefinitionRegistry registry)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Defines the initial event that triggers a new saga instance.
    /// </summary>
    /// <typeparam name="TEvent">The type of the triggering event.</typeparam>
    /// <param name="correlationKeySelector">An optional function to extract the correlation key from the event.</param>
    /// <returns>A step builder for configuring the first step's action.</returns>
    public SagaStepConfigurator<TState, TEvent, SagaBuilder<TState>> StartWith<TEvent>(Func<TEvent, string>? correlationKeySelector = null)
    {
        _initialEventType = typeof(TEvent);
        _sagaType = $"{typeof(TState).Name}_{typeof(TEvent).Name}";
        return new SagaStepConfigurator<TState, TEvent, SagaBuilder<TState>>(AddStep, correlationKeySelector);
    }

    /// <summary>
    /// Adds a subsequent step that waits for a specific event type before continuing.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to wait for.</typeparam>
    /// <param name="correlationKeySelector">An optional function to extract the correlation key from the event.</param>
    /// <returns>A step builder for configuring this step's action.</returns>
    public SagaStepConfigurator<TState, TEvent, SagaBuilder<TState>> WaitFor<TEvent>(Func<TEvent, string>? correlationKeySelector = null)
    {
        return new SagaStepConfigurator<TState, TEvent, SagaBuilder<TState>>(AddStep, correlationKeySelector);
    }

    /// <summary>
    /// Adds an alternative branch at the same logical step as the previous <see cref="WaitFor{TEvent}"/>.
    /// Use this when different event types represent alternative outcomes of the same logical step
    /// (e.g., StockReservedEvent vs. StockReservationFailedEvent).
    /// Only one of the alternatives will fire for a given saga instance.
    /// </summary>
    /// <typeparam name="TEvent">The type of event for this alternative branch.</typeparam>
    /// <param name="correlationKeySelector">An optional function to extract the correlation key from the event.</param>
    /// <returns>A step builder for configuring this alternative step's action.</returns>
    public SagaStepConfigurator<TState, TEvent, SagaBuilder<TState>> Or<TEvent>(Func<TEvent, string>? correlationKeySelector = null)
    {
        return new SagaStepConfigurator<TState, TEvent, SagaBuilder<TState>>(AddStep, correlationKeySelector, isAlternative: true);
    }

    /// <summary>
    /// Builds the saga definition with auto-complete enabled, meaning the saga completes after its last step executes.
    /// The definition is automatically registered with the registry for hosting.
    /// </summary>
    /// <returns>The completed saga definition.</returns>
    public SagaDefinition<TState> Complete()
    {
        var definition = BuildDefinition(autoComplete: true);
        _registry.Register(definition);
        return definition;
    }

    /// <summary>
    /// Builds the saga definition without auto-complete. The saga must be marked complete explicitly via <see cref="ISagaContext{TState}.MarkComplete"/>.
    /// The definition is automatically registered with the registry for hosting.
    /// </summary>
    /// <returns>The completed saga definition.</returns>
    public SagaDefinition<TState> Build()
    {
        var definition = BuildDefinition(autoComplete: false);
        _registry.Register(definition);
        return definition;
    }

    /// <summary>
    /// Adds a step to the internal step list. Handed to each <see cref="SagaStepConfigurator{TState, TEvent, TBuilder}"/>
    /// as its continuation, so the configurator never names this type.
    /// </summary>
    /// <param name="step">The step to add.</param>
    /// <returns>This builder, for further chaining.</returns>
    private SagaBuilder<TState> AddStep(SagaStep step)
    {
        _steps.Add(step);
        return this;
    }

    private SagaDefinition<TState> BuildDefinition(bool autoComplete)
    {
        if (_initialEventType is null)
            throw new InvalidOperationException("A saga must have a StartWith event defined.");

        if (_steps.Count == 0)
            throw new InvalidOperationException("A saga must have at least one step.");

        // Assign logical step indices. Consecutive steps with the same event type
        // (When/WhenNot branches) share the same logical index. Steps marked as
        // alternatives (via Or<TEvent>) also share the previous step's logical index
        // even when they have different event types.
        var logicalIndex = 0;
        _steps[0].LogicalStepIndex = logicalIndex;
        for (var i = 1; i < _steps.Count; i++)
        {
            if (_steps[i].IsAlternative)
            {
                // Alternative branches share the same logical index
                _steps[i].LogicalStepIndex = logicalIndex;
            }
            else if (_steps[i].EventType == _steps[i - 1].EventType && _steps[i].Condition is not null)
            {
                // When/WhenNot on same event type share the same logical index
                _steps[i].LogicalStepIndex = logicalIndex;
            }
            else
            {
                logicalIndex++;
                _steps[i].LogicalStepIndex = logicalIndex;
            }
        }

        return new SagaDefinition<TState>(
            _sagaType!,
            _steps.AsReadOnly(),
            autoComplete,
            _initialEventType);
    }
}
