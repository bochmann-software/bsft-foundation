namespace Bsft.Idempotency.Auditing;

/// <summary>
/// Reports an idempotency decision to everything that watches: the audit sink a host may have
/// registered, and the meters.
/// </summary>
/// <remarks>
/// One seam rather than the middleware calling two, because "report what was decided" is one
/// responsibility and the two destinations differ only in where the report goes. It also keeps the
/// decision path from having to know that a counter exists.
/// </remarks>
internal interface IIdempotencyObserver
{
    /// <summary>Reports one decision.</summary>
    /// <param name="action">What was decided.</param>
    /// <param name="scope">The caller the key belongs to.</param>
    /// <param name="key">The key the client sent.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the report has been handed on.</returns>
    Task ReportAsync(IdempotencyAuditAction action, string scope, string key, CancellationToken cancellationToken = default);
}
