using Bsft.Core;
using Microsoft.EntityFrameworkCore;

namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// Translates the provider-level optimistic-concurrency failure of a saga write into the
/// framework's <see cref="PreconditionFailedException"/>, so the self-committing store path and the
/// persistence window's commit surface a refused write identically instead of leaking a raw
/// <see cref="DbUpdateConcurrencyException"/>.
/// </summary>
internal static class SagaConcurrencyGuard
{
    /// <summary>
    /// Commits the tracked changes of <paramref name="db"/>, translating a concurrency failure into the
    /// refusal <paramref name="refuse"/> builds. The message is left to the caller so the store can name
    /// the saga while the window stays generic.
    /// </summary>
    /// <param name="db">The context whose staged changes are committed.</param>
    /// <param name="refuse">Builds the refusal thrown when the row was advanced by another writer.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes once the changes are durable.</returns>
    /// <exception cref="PreconditionFailedException">
    /// Thrown when the row the save targets was changed by another writer since it was read.
    /// </exception>
    public static async Task SaveChangesOrRefuseAsync(
        ISagaDbContext db,
        Func<PreconditionFailedException> refuse,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A writer committed between the read and this save. The token in the WHERE clause no longer
            // matches, so nothing was written; the stale write is refused, not reconciled.
            throw refuse();
        }
    }
}
