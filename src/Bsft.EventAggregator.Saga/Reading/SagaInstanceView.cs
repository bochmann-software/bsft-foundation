namespace Bsft.EventAggregator.Saga.Reading;

/// <summary>
/// One saga instance with the state it holds.
/// </summary>
/// <remarks>
/// The state is handed over in the form the store holds it rather than deserialized, because the
/// operator reading it is looking at an instance whose state type the reader has no way to know - it
/// lists across every saga at once.
/// </remarks>
/// <param name="Summary">Everything a listing shows.</param>
/// <param name="StateJson">The serialized saga state.</param>
public sealed record SagaInstanceView(SagaInstanceSummary Summary, string StateJson);
