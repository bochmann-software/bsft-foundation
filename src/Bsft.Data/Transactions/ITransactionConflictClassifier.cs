namespace Bsft.Data.Transactions;

/// <summary>
/// The provider's answer to "may the whole transaction be attempted again after this failure",
/// which the shared transaction reports through <see cref="Bsft.Core.Persistence.IAmbientTransaction.IsTransactionConflict"/>.
/// </summary>
/// <remarks>
/// <para>
/// Only the provider knows which of its errors mean that the store refused the transaction because of
/// a concurrent one — PostgreSQL's serialization failure (<c>40001</c>) and deadlock (<c>40P01</c>),
/// SQL Server's deadlock victim (<c>1205</c>) — so the classification is a seam a provider package
/// registers rather than a list this package keeps. <c>AddBsftDataPostgresTransaction</c> in
/// <c>Bsft.Data.Postgres</c> registers the PostgreSQL one.
/// </para>
/// <para>
/// Every registered classifier is asked, and a failure is a conflict when any of them says so. An
/// implementation inspects the exception and its inner exceptions, because EF Core wraps a provider
/// error in a <c>DbUpdateException</c> when it surfaces from <c>SaveChanges</c>.
/// </para>
/// </remarks>
public interface ITransactionConflictClassifier
{
    /// <summary>
    /// Reports whether the failure is a conflict with a concurrent transaction, after which a fresh
    /// attempt of the whole unit can succeed.
    /// </summary>
    /// <param name="exception">The failure the unit ended with.</param>
    /// <returns><see langword="true"/> when it is such a conflict.</returns>
    bool IsConflict(Exception exception);
}
