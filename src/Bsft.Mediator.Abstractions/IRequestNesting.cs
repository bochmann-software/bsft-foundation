namespace Bsft.Mediator;

/// <summary>
/// The marks a pipeline behavior leaves for the requests a handler dispatches inside it: whether an
/// outer request of the same dependency-injection scope, in the same asynchronous flow, is being
/// retried or holds a transaction open.
/// </summary>
/// <remarks>
/// <para>
/// The retry and transaction behaviors read these marks to treat only the outermost request as the
/// unit: a command a handler dispatches while its outer request is retried is not retried on its own,
/// and one dispatched while the outer request holds a transaction joins it.
/// </para>
/// <para>
/// Registered scoped by <c>AddBsftMediator</c>, so the marks belong to one scope: a request dispatched
/// in another scope — a background scope a handler creates — is the outermost request of its own scope.
/// A mark is set for the rest of the asynchronous method that sets it and flows into everything that
/// method awaits; the caller, and a concurrent flow of the same scope, never see it.
/// </para>
/// </remarks>
public interface IRequestNesting
{
    /// <summary>
    /// Gets a value indicating whether an outer request of this scope, in this asynchronous flow, is
    /// being retried.
    /// </summary>
    bool IsRetrying { get; }

    /// <summary>
    /// Gets a value indicating whether an outer request of this scope, in this asynchronous flow, holds
    /// a transaction open.
    /// </summary>
    bool IsTransactionOpen { get; }

    /// <summary>
    /// Marks the request being handled as retried, for the rest of the calling asynchronous method and
    /// every request it dispatches.
    /// </summary>
    void MarkRetrying();

    /// <summary>
    /// Marks the request being handled as holding a transaction open, for the rest of the calling
    /// asynchronous method and every request it dispatches.
    /// </summary>
    void MarkTransactionOpen();
}
