namespace Bsft.EventAggregator.Saga.Reading;

/// <summary>
/// How a saga listing is ordered.
/// </summary>
/// <remarks>
/// A short, closed set rather than a sort expression: each member names a column the table is indexed
/// on, so a listing cannot be made to scan by asking for an order that nothing supports. The last
/// transition is the default, because it is what a stalled instance is recognised by.
/// </remarks>
public enum SagaInstanceOrder
{
    /// <summary>By last transition, most recent first.</summary>
    UpdatedDescending = 0,

    /// <summary>By last transition, longest untouched first. What a stall hunt reads in.</summary>
    UpdatedAscending = 1,

    /// <summary>By start, newest first.</summary>
    CreatedDescending = 2,

    /// <summary>By start, oldest first.</summary>
    CreatedAscending = 3,
}
