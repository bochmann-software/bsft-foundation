using Bsft.Messaging.Consuming;
using Bsft.Outbox;

namespace Bsft.Messaging.Tests.Fakes;

/// <summary>
/// A schedule that answers with one fixed delay and records what it was asked, so a test can assert the
/// instant a caller derived from it without the real schedule's jitter.
/// </summary>
/// <remarks>
/// Reading a requested wait out of a failure is <em>not</em> stubbed: it is delegated to the real
/// implementation, because that is the half the callers are supposed to route a handler's ask through
/// and a stub would let a caller that never passes the failure on still pass its test.
/// </remarks>
public sealed class FakeMessageRetrySchedule : IMessageRetrySchedule
{
    private readonly MessageRetrySchedule _real = new(new OutboxRetrySchedule(new Random(Seed: 1)));

    /// <summary>The delay every call is answered with.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(7);

    /// <summary>What each caller asked for, in order.</summary>
    public List<(int AttemptNumber, TimeSpan BackoffBase, TimeSpan MaxDelay, TimeSpan? RetryAfter)> Requests { get; } =
        [];

    /// <summary>The failures a caller read a requested wait out of, in order.</summary>
    public List<Exception> Failures { get; } = [];

    public TimeSpan NextDelay(int attemptNumber, TimeSpan backoffBase, TimeSpan maxDelay, TimeSpan? retryAfter)
    {
        Requests.Add((attemptNumber, backoffBase, maxDelay, retryAfter));

        return Delay;
    }

    public TimeSpan? RequestedRetryAfter(Exception failure)
    {
        Failures.Add(failure);

        return _real.RequestedRetryAfter(failure);
    }
}
