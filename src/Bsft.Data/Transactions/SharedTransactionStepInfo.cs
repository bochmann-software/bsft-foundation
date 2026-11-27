using System.Data.Common;

namespace Bsft.Data.Transactions;

/// <summary>A step the shared transaction runs on the scope's connection, as an <see cref="ISharedTransactionStepObserver"/> is told.</summary>
/// <param name="Kind">What the step does.</param>
/// <param name="Connection">The scope's shared connection the step runs on.</param>
public readonly record struct SharedTransactionStepInfo(SharedTransactionStepKind Kind, DbConnection Connection);
