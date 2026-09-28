namespace Bsft.Core.Persistence;

/// <summary>
/// Commits whatever the caller staged, so a package that writes through somebody else's storage can
/// close its own unit of work without knowing what that storage is.
/// </summary>
/// <remarks>
/// <para>
/// Several packages stage rather than write: publishing a notification adds the dispatch rows and the
/// outbox events to the caller's change tracker, the shipped audit sink adds its row to the caller's
/// context, and a store that is handed the host's context saves nothing of its own. That contract is
/// what ties an announcement to the change it announces — a mail about a change that rolled back is
/// never sent — and it leaves one obligation behind: something has to commit. A request commits at the
/// end of the request; a background sweep, which announces nothing it wrote itself, has to commit for
/// itself or its rows are discarded with the scope.
/// </para>
/// <para>
/// This is that seam, stated once for the whole framework. It lives in <c>Bsft.Core</c> because that is
/// the one package with no package references at all, which is the property that lets an
/// <c>.Abstractions</c> package take it without widening the closure it promises its consumers. It is
/// deliberately not spelled as EF Core: an implementation over a dictionary, a document store or a
/// context this framework never sees is as valid as the shipped one, and a contract mentioning
/// <c>DbContext</c> would put a persistence choice in front of every package that only needs a commit.
/// </para>
/// <para>
/// An implementation is expected to be a no-op when nothing was staged, and to be safe to call from a
/// scope that staged nothing at all.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Commits whatever was staged in the current scope.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task that completes when the staged work is durable.</returns>
    /// <remarks>
    /// A failure surfaces to the caller rather than being swallowed, so a caller that already answered
    /// success cannot be the one calling this: the commit belongs before the answer, not after it.
    /// </remarks>
    Task CommitAsync(CancellationToken cancellationToken = default);
}
