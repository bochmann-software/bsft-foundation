using Bsft.Data.Transactions;

namespace Bsft.Data.Tests.Fakes;

/// <summary>Classifies exactly the exceptions of one type as transient.</summary>
public sealed class FakeTransactionConflictClassifier<TException> : ITransactionConflictClassifier
    where TException : Exception
{
    public bool IsConflict(Exception exception) => exception is TException;
}
