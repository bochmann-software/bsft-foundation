using Bsft.Core.Persistence;

namespace Bsft.Resilience.Mediator.Tests.Fakes;

/// <summary>An ambient transaction whose state and classification a test sets.</summary>
public sealed class FakeAmbientTransaction : IAmbientTransaction
{
    public bool IsActive { get; set; }

    /// <summary>The exception type the fake's provider classifies as a transaction conflict.</summary>
    public Type? ConflictType { get; set; }

    public bool IsTransactionConflict(Exception exception) => exception.GetType() == ConflictType;

    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default) =>
        work(cancellationToken);
}
