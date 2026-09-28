namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Represents a single step in a saga definition, including the event type it responds to,
/// the correlation key selector, the action to execute, and an optional condition predicate.
/// </summary>
public sealed class SagaStep
{
    /// <summary>
    /// Gets the event type that triggers this step.
    /// </summary>
    public Type EventType { get; }

    /// <summary>
    /// Gets the delegate that extracts a correlation key from the event.
    /// May be <c>null</c> if no correlation key is required.
    /// </summary>
    public Delegate? CorrelationKeySelector { get; }

    /// <summary>
    /// Gets the action delegate to execute when this step is triggered.
    /// The delegate signature depends on the saga state type and event type.
    /// </summary>
    public Delegate Action { get; }

    /// <summary>
    /// Gets the optional condition that must be met for the action to execute.
    /// May be <c>null</c> if the step is unconditional.
    /// </summary>
    public SagaCondition? Condition { get; }

    /// <summary>
    /// Gets the logical step index used for matching against <see cref="SagaInstance{TState}.CurrentStepIndex"/>.
    /// Multiple steps with the same event type and different conditions (When/WhenNot branches)
    /// share the same logical index so they are all evaluated when that step is reached.
    /// Steps marked as alternatives (via <see cref="SagaBuilder{TState}.Or{TEvent}"/>) also share
    /// the same logical index even when they have different event types.
    /// </summary>
    public int LogicalStepIndex { get; internal set; }

    /// <summary>
    /// Gets or sets a value indicating whether this step is an alternative branch
    /// that shares the logical step index with the preceding step.
    /// </summary>
    public bool IsAlternative { get; internal set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaStep"/> class.
    /// </summary>
    /// <param name="eventType">The event type that triggers this step.</param>
    /// <param name="correlationKeySelector">The delegate that extracts a correlation key from the event.</param>
    /// <param name="action">The action delegate to execute.</param>
    /// <param name="condition">The optional condition for this step.</param>
    public SagaStep(Type eventType, Delegate? correlationKeySelector, Delegate action, SagaCondition? condition = null)
    {
        EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
        Action = action ?? throw new ArgumentNullException(nameof(action));
        CorrelationKeySelector = correlationKeySelector;
        Condition = condition;
        Invoke = SagaDelegateInvoker.For<Task>(action);
        EvaluateCondition = condition is null ? null : SagaDelegateInvoker.For<bool>(condition.Predicate);
    }

    /// <summary>
    /// Calls <see cref="Action"/> with a context and an event, both as <see cref="object"/>.
    /// </summary>
    /// <remarks>
    /// Compiled once here, at definition time, rather than reached through
    /// <see cref="Delegate.DynamicInvoke"/> on every delivered event. A saga definition is built once
    /// and then dispatched against for the life of the process, so this is the side to pay on.
    /// </remarks>
    internal Func<object?, object?, Task> Invoke { get; }

    /// <summary>
    /// Calls <see cref="SagaCondition.Predicate"/> with an event and a state, or <see langword="null"/>
    /// where the step is unconditional. Compiled once for the same reason as <see cref="Invoke"/>.
    /// </summary>
    /// <remarks>
    /// It lives here rather than on <see cref="SagaCondition"/> because that is a record: a
    /// <see langword="with"/> expression replacing the predicate would leave a compiled caller for the
    /// old one behind, and a condition that quietly answers the previous question is worse than one
    /// that costs a reflective call.
    /// </remarks>
    internal Func<object?, object?, bool>? EvaluateCondition { get; }
}
