namespace Bsft.EventAggregator.Saga.Reading;

/// <summary>
/// One saga instance as a listing shows it: which saga, which instance, how far it got and when it
/// last moved.
/// </summary>
/// <remarks>
/// No state. A saga's state carries whatever the saga put in it, so it is graded apart under
/// <see cref="SagaInstanceView"/> rather than shown to everyone who may see that a flow is stalled.
/// The state's type name is here, because knowing which shape a stuck instance holds is part of
/// recognising it and is not the data itself.
/// </remarks>
/// <param name="Id">The row's identity, which is what the detail route is addressed by.</param>
/// <param name="SagaType">The saga definition this instance belongs to.</param>
/// <param name="CorrelationKey">What identifies the instance within its type.</param>
/// <param name="StateTypeName">The CLR type the state is held as.</param>
/// <param name="CurrentStepIndex">How far through the definition the instance got.</param>
/// <param name="IsComplete">Whether the instance has finished.</param>
/// <param name="CreatedAt">When the instance started.</param>
/// <param name="UpdatedAt">When it last moved.</param>
public sealed record SagaInstanceSummary(
    int Id,
    string SagaType,
    string CorrelationKey,
    string StateTypeName,
    int CurrentStepIndex,
    bool IsComplete,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
