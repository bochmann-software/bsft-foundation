namespace Bsft.EventAggregator.Saga.Timeouts;

/// <summary>
/// The kind of timeout operation a saga step requested.
/// </summary>
internal enum PendingSagaTimeoutKind
{
    Schedule,
    Cancel,
    CancelAll,
}
