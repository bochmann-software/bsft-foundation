namespace Bsft.Data.Transactions;

/// <summary>A step the shared transaction runs on the scope's connection itself, outside EF Core.</summary>
public enum SharedTransactionStepKind
{
    /// <summary>Opening the connection, when the outermost unit finds it closed.</summary>
    Open,

    /// <summary>Beginning the transaction of the outermost unit.</summary>
    Begin,

    /// <summary>Asking the store whether it already aborted the transaction, when a provider registered a probe.</summary>
    AbortProbe,

    /// <summary>Committing the transaction.</summary>
    Commit,

    /// <summary>Disposing a transaction that did not commit, which rolls it back.</summary>
    Rollback,

    /// <summary>Disposing a transaction after it committed.</summary>
    Release,

    /// <summary>Closing the connection the outermost unit opened.</summary>
    Close,
}
