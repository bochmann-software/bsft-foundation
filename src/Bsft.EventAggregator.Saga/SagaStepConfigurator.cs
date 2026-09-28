namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Provides a fluent API for configuring an individual saga step, including its action and optional conditions.
/// </summary>
/// <typeparam name="TState">The type of the saga state.</typeparam>
/// <typeparam name="TEvent">The type of event this step responds to.</typeparam>
/// <typeparam name="TBuilder">
/// The builder the chain continues with once the step is configured, which is the
/// <see cref="SagaBuilder{TState}"/> that created this configurator.
/// </typeparam>
/// <remarks>
/// The builder is a type argument rather than a field of type <see cref="SagaBuilder{TState}"/> so that
/// the dependency runs one way: the builder creates configurators and hands each one its continuation,
/// and a configurator only calls that continuation.
/// </remarks>
public sealed class SagaStepConfigurator<TState, TEvent, TBuilder> where TState : new()
{
    private readonly Func<SagaStep, TBuilder> _addStep;
    private readonly Delegate? _correlationKeySelector;
    private readonly Type _eventType;
    private readonly bool _isAlternative;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaStepConfigurator{TState, TEvent, TBuilder}"/> class.
    /// </summary>
    /// <param name="addStep">Adds the configured step to the parent builder and returns that builder.</param>
    /// <param name="correlationKeySelector">The correlation key selector for this step.</param>
    /// <param name="isAlternative">Whether this step is an alternative branch sharing the previous step's logical index.</param>
    internal SagaStepConfigurator(Func<SagaStep, TBuilder> addStep, Func<TEvent, string>? correlationKeySelector, bool isAlternative = false)
    {
        _addStep = addStep ?? throw new ArgumentNullException(nameof(addStep));
        _correlationKeySelector = correlationKeySelector;
        _eventType = typeof(TEvent);
        _isAlternative = isAlternative;
    }

    /// <summary>
    /// Specifies the unconditional action to execute for this step.
    /// </summary>
    /// <param name="action">The async action receiving the saga context and the triggering event.</param>
    /// <returns>The parent saga builder for further chaining.</returns>
    public TBuilder Then(Func<ISagaContext<TState>, TEvent, Task> action)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        var step = new SagaStep(_eventType, _correlationKeySelector, action) { IsAlternative = _isAlternative };
        return _addStep(step);
    }

    /// <summary>
    /// Specifies a conditional action that executes only when the predicate evaluates to <c>true</c>.
    /// </summary>
    /// <param name="predicate">The condition that must be met.</param>
    /// <param name="action">The async action to execute when the condition is met.</param>
    /// <returns>The parent saga builder for further chaining.</returns>
    public TBuilder When(Func<TEvent, TState, bool> predicate, Func<ISagaContext<TState>, TEvent, Task> action)
    {
        if (predicate is null)
            throw new ArgumentNullException(nameof(predicate));
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        var step = new SagaStep(_eventType, _correlationKeySelector, action, new SagaCondition(predicate, IsNegated: false)) { IsAlternative = _isAlternative };
        return _addStep(step);
    }

    /// <summary>
    /// Specifies a conditional action that executes only when the predicate evaluates to <c>false</c>.
    /// </summary>
    /// <param name="predicate">The condition that must NOT be met.</param>
    /// <param name="action">The async action to execute when the condition is not met.</param>
    /// <returns>The parent saga builder for further chaining.</returns>
    public TBuilder WhenNot(Func<TEvent, TState, bool> predicate, Func<ISagaContext<TState>, TEvent, Task> action)
    {
        if (predicate is null)
            throw new ArgumentNullException(nameof(predicate));
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        var step = new SagaStep(_eventType, _correlationKeySelector, action, new SagaCondition(predicate, IsNegated: true)) { IsAlternative = _isAlternative };
        return _addStep(step);
    }
}
