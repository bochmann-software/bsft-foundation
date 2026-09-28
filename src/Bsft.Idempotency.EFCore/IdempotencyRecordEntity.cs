namespace Bsft.Idempotency.EFCore;

/// <summary>
/// One idempotency key, and what is known about the request that claimed it.
/// </summary>
/// <remarks>
/// The row's existence is what makes a repeated request a replay, so this table is the client-facing
/// edge's deduplication in the way <c>InboxMessages</c> is the integration-event edge's. It is
/// written twice per protected request — once to reserve, once to complete — and deleted by
/// <c>IdempotencyRecordSweeper</c> once it decides nothing.
/// </remarks>
public sealed class IdempotencyRecordEntity
{
    /// <summary>Auto-increment primary key.</summary>
    public long Id { get; set; }

    /// <summary>
    /// Which caller the key belongs to: the digest of tenant, subject and client id. It is the
    /// leading half of the unique key, and it is the cross-tenant boundary — a key guessed exactly
    /// still lands in a different scope and is therefore a miss.
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>The value the client sent in the idempotency header.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The digest of the request that claimed the key. A repeat whose fingerprint differs is
    /// refused rather than answered from this row.
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Whether the request is still running or has finished.</summary>
    public IdempotencyRecordState State { get; set; }

    /// <summary>
    /// The UTC instant after which the row decides nothing: the lease while reserved, the retention
    /// once completed. One column rather than two, because the two never apply at once, and because
    /// it lets the pruner sweep on a single predicate that cannot delete a reservation still in
    /// force.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>The status the completed request answered with, null while it is running.</summary>
    public int? StatusCode { get; set; }

    /// <summary>The allowlisted response headers, as JSON, null while the request is running.</summary>
    public string? ResponseHeaders { get; set; }

    /// <summary>
    /// The response body as it was produced, null while the request is running. Bytes rather than
    /// text, because a replay has to be byte-identical.
    /// </summary>
    public byte[]? ResponseBody { get; set; }
}
