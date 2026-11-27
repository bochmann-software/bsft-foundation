using Bsft.Core.Persistence;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests.Persistence;

public sealed class AmbientTransactionTests
{
    [Fact]
    public void FindTransactionConflict_ByDefault_WhenTheFailureIsAConflict_ReturnsTheFailure()
    {
        var timeoutException = new TimeoutException();
        IAmbientTransaction transaction = new FakeAmbientTransaction();

        var result = transaction.FindTransactionConflict(timeoutException);

        result.Should().BeSameAs(timeoutException);
    }

    [Fact]
    public void FindTransactionConflict_ByDefault_WhenTheFailureIsNoConflict_ReturnsNull()
    {
        var invalidOperationException = new InvalidOperationException();
        IAmbientTransaction transaction = new FakeAmbientTransaction();

        var result = transaction.FindTransactionConflict(invalidOperationException);

        result.Should().BeNull();
    }

    private sealed class FakeAmbientTransaction : IAmbientTransaction
    {
        public bool IsActive => false;

        public bool IsTransactionConflict(Exception exception)
        {
            return exception is TimeoutException;
        }

        public Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken = default)
        {
            return work(cancellationToken);
        }
    }
}
