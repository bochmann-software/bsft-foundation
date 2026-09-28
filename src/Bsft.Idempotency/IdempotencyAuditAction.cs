namespace Bsft.Idempotency;

/// <summary>
/// What an audit sink is being told about.
/// </summary>
/// <remarks>
/// Only the three decisions worth an operator's attention are reported. A request that simply ran is
/// not one of them: that is the ordinary case and auditing it would double the write volume of every
/// protected endpoint for no signal.
/// </remarks>
public enum IdempotencyAuditAction
{
    /// <summary>A stored response was served again instead of the request running.</summary>
    Replayed = 0,

    /// <summary>
    /// A key was presented with a different request than the one it names. It is the interesting one:
    /// a client generating keys wrongly, or somebody probing another caller's keys.
    /// </summary>
    KeyReused = 1,

    /// <summary>A request was refused because the first request under its key was still running.</summary>
    InProgress = 2,
}
