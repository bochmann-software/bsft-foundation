using System.Data.Common;
using Bsft.Data.Transactions;

namespace Bsft.Data.Tests.Fakes;

/// <summary>Answers every probe with the same abort, or with none, and counts the questions.</summary>
public sealed class FakeTransactionAbortProbe(Exception? abort) : ITransactionAbortProbe
{
    public int Probes { get; private set; }

    public Task<Exception?> FindAbortAsync(DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        Probes++;

        return Task.FromResult(abort);
    }
}
