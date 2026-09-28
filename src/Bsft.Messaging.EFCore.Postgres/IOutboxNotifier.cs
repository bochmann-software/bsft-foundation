using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Postgres;

/// <summary>
/// Announces on the database that outbox rows were written, so a listening dispatcher drains at once
/// instead of waiting for its next poll.
/// </summary>
/// <remarks>
/// <para>
/// The announcement is made through the context that wrote the rows, which is what puts it on the
/// same connection and therefore inside the same transaction whenever the caller opened one. A
/// PostgreSQL notification raised inside a transaction is delivered when that transaction commits and
/// is discarded when it rolls back, so a listener is never woken for rows that do not exist.
/// </para>
/// <para>
/// Every implementation is best-effort by contract: a notification that cannot be sent is reported and
/// swallowed rather than thrown. The rows are already committed at that point, and the dispatcher's
/// own poll interval — not this — is what guarantees they are delivered. Failing the caller's save
/// over a wake-up it does not need would turn an optimisation into an outage.
/// </para>
/// <para>
/// It is declared here rather than in <c>Bsft.Messaging.Abstractions</c> under the exception
/// docs/package-conventions.md section 1 states for exactly this shape: its signature is written in
/// terms of <see cref="DbContext"/>, and the dependency closure of the family's contract package —
/// the BCL and <c>Microsoft.Extensions.*.Abstractions</c> — is part of that package's contract and is
/// pinned by a test. It is public because the interceptor a host adds to its context takes it, and a
/// public type cannot take an internal one.
/// </para>
/// </remarks>
public interface IOutboxNotifier
{
    /// <summary>Announces the write synchronously.</summary>
    /// <param name="context">The context that wrote the outbox rows.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    void Notify(DbContext context);

    /// <summary>Announces the write.</summary>
    /// <param name="context">The context that wrote the outbox rows.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the announcement was made or given up on.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    Task NotifyAsync(DbContext context, CancellationToken cancellationToken = default);
}
