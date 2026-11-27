using Bsft.Data.Transactions;
using Npgsql;

namespace Bsft.Data.Postgres;

/// <summary>
/// Classifies PostgreSQL's serialization failure (<c>40001</c>) and deadlock (<c>40P01</c>) as transaction conflicts,
/// so a shared transaction that ended with either is retried as a whole.
/// </summary>
/// <remarks>
/// <para>
/// Those two are the codes PostgreSQL documents as "retry the whole transaction": the server aborted it
/// because a concurrent transaction won, and running the same work again in a fresh transaction can
/// succeed. Npgsql's broader <c>PostgresException.IsTransient</c> is deliberately not used — it also
/// covers connection and resource failures, which a whole-unit retry inside the request does not fix
/// and which the host's own retry predicate already decides on.
/// </para>
/// <para>
/// The exception chain is walked, because EF Core wraps the server error in a <c>DbUpdateException</c>
/// when it surfaces from <c>SaveChanges</c>, and a handler may wrap it again.
/// </para>
/// </remarks>
internal sealed class PostgresTransactionConflictClassifier : ITransactionConflictClassifier
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
    public bool IsConflict(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
                return true;
        }

        return false;
    }
}
