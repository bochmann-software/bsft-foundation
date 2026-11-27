namespace Bsft.Outbox.EFCore;

/// <summary>
/// Narrows the rows the lease protocol may see in one scope, for an outbox whose table holds rows of
/// several owners, such as the tenants of a tenant-keyed table.
/// </summary>
/// <typeparam name="TEntity">The entity that holds the rows of this outbox.</typeparam>
/// <remarks>
/// An <see cref="IOutboxRowSource{TEntity, TPayload}"/> implements this beside its own interface when it
/// needs it; <see cref="EFCoreOutboxStore{TEntity, TPayload}"/> then runs every statement of the protocol,
/// the poll, the claim and the three settling writes, over the narrowed query, and restricts the claim
/// to the owner of the row it claims. A source that does not
/// implement it is used over the whole set, as before.
/// </remarks>
public interface IOutboxRowScope<TEntity>
    where TEntity : OutboxRow
{
    /// <summary>Returns the rows of a set that the scope may poll and settle.</summary>
    /// <param name="rows">The set, from <see cref="IOutboxRowSource{TEntity, TPayload}.Rows"/>.</param>
    /// <param name="scopedServices">The scope the set was resolved from.</param>
    /// <returns>The narrowed query, or <paramref name="rows"/> itself.</returns>
    IQueryable<TEntity> InScope(IQueryable<TEntity> rows, IServiceProvider scopedServices);

    /// <summary>
    /// Restricts a query to the owner of a row that was read, so a claim by id cannot reach a row of
    /// another owner that carries the same id.
    /// </summary>
    /// <param name="rows">The query the claim is built on.</param>
    /// <param name="row">The row the claim is for, as the poll read it.</param>
    /// <returns>The restricted query, or <paramref name="rows"/> itself.</returns>
    /// <remarks>
    /// The settling writes need no such restriction: they match the lease token the claim wrote, which
    /// no other row carries.
    /// </remarks>
    IQueryable<TEntity> WithOwnerOf(IQueryable<TEntity> rows, TEntity row);
}
