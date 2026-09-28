using Microsoft.EntityFrameworkCore;

namespace Bsft.Outbox.EFCore;

/// <summary>
/// Where one outbox's rows live and how a row is read: the two things the lease protocol cannot know
/// and the only two an outbox has to supply to reuse it.
/// </summary>
/// <typeparam name="TEntity">The entity that holds the rows of this outbox.</typeparam>
/// <typeparam name="TPayload">What those rows carry.</typeparam>
/// <remarks>
/// The set is taken from a scope rather than held, because the store outlives any one context: it
/// serves a long-running drainer and must not keep a context open between passes.
/// </remarks>
public interface IOutboxRowSource<TEntity, out TPayload>
    where TEntity : OutboxRow
{
    /// <summary>Gets the column length of the recorded failure, which a reason is cut to.</summary>
    /// <remarks>
    /// The reason comes from an exception message, whose length nothing bounds. Cutting it here is
    /// what keeps the very write that records why a row failed from failing itself.
    /// </remarks>
    int FailureReasonMaxLength { get; }

    /// <summary>Reaches this outbox's set through a scope.</summary>
    /// <param name="scopedServices">The scope the context is resolved from.</param>
    /// <returns>The set the rows live in.</returns>
    DbSet<TEntity> Rows(IServiceProvider scopedServices);

    /// <summary>Reads what a row carries.</summary>
    /// <param name="row">The row as it was read.</param>
    /// <returns>The payload a side effect is applied to.</returns>
    TPayload PayloadOf(TEntity row);
}
