namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Represents a conditional predicate for a saga step, along with a flag indicating
/// whether the predicate result should be negated (WhenNot behavior).
/// </summary>
/// <param name="Predicate">The condition delegate that evaluates to <c>true</c> or <c>false</c>.</param>
/// <param name="IsNegated">Whether the predicate result should be inverted.</param>
public sealed record SagaCondition(Delegate Predicate, bool IsNegated);
