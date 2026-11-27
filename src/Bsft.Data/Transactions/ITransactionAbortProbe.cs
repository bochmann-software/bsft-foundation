using System.Data.Common;

namespace Bsft.Data.Transactions;

/// <summary>
/// The provider's answer to "has the store already aborted this transaction", asked by the shared
/// transaction right before it commits.
/// </summary>
/// <remarks>
/// <para>
/// A failure the interceptor sees — a command that failed or was cancelled — already stops the commit.
/// A failure raised while EF Core reads the rows of a query that started fine (a division by zero in a
/// later row, a statement timeout, a serialization failure on read) reaches no interceptor, and a
/// handler that swallows it leaves a transaction the store aborted: PostgreSQL answers the
/// <c>COMMIT</c> of such a transaction with a silent <c>ROLLBACK</c>. The probe closes that gap by
/// asking the store before the commit, so the unit fails instead of reporting success for work that
/// was never persisted.
/// </para>
/// <para>
/// The probe is a seam a provider package registers, like <see cref="ITransactionConflictClassifier"/>;
/// <c>AddBsftDataPostgresTransaction</c> in <c>Bsft.Data.Postgres</c> registers the PostgreSQL one,
/// which costs one <c>SELECT 1</c> round trip per committed unit. Without a registered probe the
/// shared transaction commits without asking.
/// </para>
/// </remarks>
public interface ITransactionAbortProbe
{
    /// <summary>
    /// Asks the store whether <paramref name="transaction"/> can still commit.
    /// </summary>
    /// <param name="transaction">The open shared transaction, on its open connection.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// The store's error saying the transaction is aborted, or <see langword="null"/> when it can still
    /// commit.
    /// </returns>
    Task<Exception?> FindAbortAsync(DbTransaction transaction, CancellationToken cancellationToken = default);
}
