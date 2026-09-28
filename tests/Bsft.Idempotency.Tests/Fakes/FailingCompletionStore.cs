namespace Bsft.Idempotency.Tests.Fakes;

/// <summary>
/// A store that reserves and releases normally and cannot write the finished record, which is what a
/// shared store looks like when the database is reachable at the start of a request and gone by the
/// end of it.
/// </summary>
internal sealed class FailingCompletionStore : IIdempotencyStore
{
    private readonly IIdempotencyStore _inner;

    /// <summary>Initializes a new instance of the <see cref="FailingCompletionStore"/> class.</summary>
    /// <param name="inner">The store that does the real work of reserving and releasing.</param>
    public FailingCompletionStore(IIdempotencyStore inner) => _inner = inner;

    /// <summary>Gets a value indicating whether the completion was attempted.</summary>
    public bool WasCompletionAttempted { get; private set; }

    /// <inheritdoc />
    public Task<IdempotencyBeginResult> BeginAsync(
        IdempotencyReservationRequest request, CancellationToken ct = default)
        => _inner.BeginAsync(request, ct);

    /// <inheritdoc />
    public Task CompleteAsync(IdempotencyCompletionRequest request, CancellationToken ct = default)
    {
        WasCompletionAttempted = true;

        throw new InvalidOperationException("The record store is unreachable.");
    }

    /// <inheritdoc />
    public Task ReleaseAsync(string scope, string key, string fingerprint, CancellationToken ct = default)
        => _inner.ReleaseAsync(scope, key, fingerprint, ct);
}
