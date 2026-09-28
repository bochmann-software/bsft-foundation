namespace Bsft.EventAggregator.Saga.Reading;

/// <summary>
/// What an operator narrows a saga listing by.
/// </summary>
/// <remarks>
/// Every criterion is optional and they combine with AND. The one that answers the question this
/// listing exists for is <see cref="UpdatedBefore"/> together with <c>IsComplete: false</c>: a saga
/// that has not moved in an hour is stalled, whether or not a timeout happens to be due.
/// </remarks>
/// <param name="SagaType">The exact saga type to restrict to.</param>
/// <param name="SagaTypePrefix">A saga-type prefix, for a whole family of sagas.</param>
/// <param name="CorrelationKey">The correlation key to restrict to.</param>
/// <param name="StateTypeName">The state type to restrict to.</param>
/// <param name="IsComplete">Whether to restrict to finished instances, or to running ones.</param>
/// <param name="CreatedAfter">The earliest start to include.</param>
/// <param name="CreatedBefore">The latest start to include.</param>
/// <param name="UpdatedAfter">The earliest transition to include.</param>
/// <param name="UpdatedBefore">The latest transition to include.</param>
/// <param name="MinStepIndex">The earliest step an included instance must have reached.</param>
public sealed record SagaInstanceFilter(
    string? SagaType = null,
    string? SagaTypePrefix = null,
    string? CorrelationKey = null,
    string? StateTypeName = null,
    bool? IsComplete = null,
    DateTimeOffset? CreatedAfter = null,
    DateTimeOffset? CreatedBefore = null,
    DateTimeOffset? UpdatedAfter = null,
    DateTimeOffset? UpdatedBefore = null,
    int? MinStepIndex = null);
