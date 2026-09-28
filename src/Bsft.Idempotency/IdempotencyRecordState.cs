namespace Bsft.Idempotency;

/// <summary>
/// What a stored idempotency record says about the request it stands for.
/// </summary>
/// <remarks>
/// The state machine is <c>Reserved → (Completed | Failed | Abandoned)</c>, and only two of those
/// four are values here, on purpose. <em>Failed</em> is not a stored state: a request that ended in
/// a server error, or in a status the host declared non-replayable, has its record deleted, because
/// a client must be able to retry it — a row that remembered the failure would refuse the retry the
/// header exists to permit. <em>Abandoned</em> is not a stored state either: it is what a
/// <see cref="Reserved"/> record past its lease is called, computed from the lease rather than
/// written by anybody, because the process that would have written it is the process that crashed.
/// </remarks>
public enum IdempotencyRecordState
{
    /// <summary>
    /// The key is claimed and the request is running. A second request under this key is refused
    /// with 409 until the record completes or its lease lapses.
    /// </summary>
    Reserved = 0,

    /// <summary>
    /// The request finished and its response is stored. A second request under this key is answered
    /// with that response until the retention expires.
    /// </summary>
    Completed = 1,
}
