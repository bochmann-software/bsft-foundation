namespace Bsft.Data.Transactions;

/// <summary>
/// Is told when the shared transaction starts and completes a step it runs on the scope's connection
/// itself, outside EF Core: opening and closing the connection, beginning, probing, committing, rolling
/// back and releasing the transaction.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's interceptors see every command, save and savepoint of an enlisted context, but not these
/// steps: the outermost unit runs them directly on the <see cref="System.Data.Common.DbConnection"/> and
/// <see cref="System.Data.Common.DbTransaction"/>. A host that admits one operation on the connection at a
/// time registers an observer, scoped, so that these steps pass the same gate. Savepoints and the saves of
/// the enlisted contexts go through EF Core and are not reported here. Without a registered observer the
/// shared transaction behaves exactly as it did before observers existed.
/// </para>
/// <para>
/// Zero or more observers are resolved as <c>IEnumerable&lt;ISharedTransactionStepObserver&gt;</c> from the
/// scope of the shared transaction. For one step <see cref="OnStepStarting"/> runs in registration order
/// and <see cref="OnStepCompleted"/> in reverse order, and only for the observers whose
/// <see cref="OnStepStarting"/> returned. Steps never overlap: every step completes before the next starts.
/// </para>
/// <para>
/// An exception from <see cref="OnStepStarting"/> refuses a step that can still be left out —
/// <see cref="SharedTransactionStepKind.Open"/>, <see cref="SharedTransactionStepKind.Begin"/>,
/// <see cref="SharedTransactionStepKind.AbortProbe"/> and <see cref="SharedTransactionStepKind.Commit"/>:
/// the step does not run and the exception propagates unchanged, unwrapped; a refused commit ends the
/// unit through the rollback path like a failed one. The steps that end a unit —
/// <see cref="SharedTransactionStepKind.Rollback"/>, <see cref="SharedTransactionStepKind.Release"/> and
/// <see cref="SharedTransactionStepKind.Close"/> — run anyway, so the transaction is always released and
/// the connection always closed, and the exception is logged as a warning, never thrown: after a commit,
/// throwing would report a persisted unit as failed and let a retry write it twice, and a unit that did
/// not commit already fails with an exception of its own. An exception from
/// <see cref="OnStepCompleted"/> is logged as a warning and ignored.
/// </para>
/// <para>
/// An observer decides at once: it admits the step by returning or refuses it by throwing, and it never
/// waits for another operation to end — no blocking <c>Wait</c>, <c>Result</c> or lock held by foreign
/// work — because the callbacks run synchronously on the unit's own flow, between the steps of an open
/// transaction. Neither callback may use the scope's connection or transaction itself, which the step it
/// is told about is about to occupy or has just released.
/// </para>
/// </remarks>
public interface ISharedTransactionStepObserver
{
    /// <summary>Called right before the shared transaction runs <paramref name="step"/> on the connection.</summary>
    /// <param name="step">The step about to run.</param>
    void OnStepStarting(SharedTransactionStepInfo step);

    /// <summary>
    /// Called after <paramref name="step"/> ended, whether it succeeded, failed or was cancelled, for every
    /// observer whose <see cref="OnStepStarting"/> returned. Must not throw; an exception is logged and ignored.
    /// </summary>
    /// <param name="step">The step that ended.</param>
    void OnStepCompleted(SharedTransactionStepInfo step);
}
