namespace Bsft.Idempotency.Diagnostics;

/// <summary>
/// Counts the idempotency decisions, so an operator can see whether the protection is doing
/// anything and whether clients are using it correctly.
/// </summary>
internal interface IIdempotencyTelemetry
{
    /// <summary>Records one decision.</summary>
    /// <param name="action">Which decision was taken.</param>
    void Record(IdempotencyAuditAction action);
}
