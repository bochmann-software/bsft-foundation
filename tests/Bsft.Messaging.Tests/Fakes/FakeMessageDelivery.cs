using Bsft.Messaging.Consuming;

namespace Bsft.Messaging.Tests.Fakes;

/// <summary>Records which of the three completions the consumer chose, and what it said.</summary>
public sealed class FakeMessageDelivery : IMessageDelivery
{
    private readonly List<string> _parked = [];

    public FakeMessageDelivery(IntegrationEventMessage message, int deliveryAttempt = 1)
    {
        Message = message;
        DeliveryAttempt = deliveryAttempt;
    }

    public IntegrationEventMessage Message { get; }

    public int DeliveryAttempt { get; }

    public int Acknowledged { get; private set; }

    public int Retried { get; private set; }

    /// <summary>The delays the consumer asked for, one per retry, in order.</summary>
    public List<TimeSpan> RetryDelays { get; } = [];

    /// <summary>The token the consumer handed to the last retry, which is how a shutdown reaches a
    /// transport that is waiting one out.</summary>
    public CancellationToken LastRetryToken { get; private set; }

    public IReadOnlyList<string> ParkReasons => _parked;

    /// <summary>Set to make every completion fail, as a transport that has lost its connection would.</summary>
    public Exception? CompletionFailure { get; set; }

    public Task AcknowledgeAsync(CancellationToken ct = default)
    {
        if (CompletionFailure is not null)
            return Task.FromException(CompletionFailure);

        Acknowledged++;
        return Task.CompletedTask;
    }

    public Task RetryAsync(TimeSpan delay, CancellationToken ct = default)
    {
        if (CompletionFailure is not null)
            return Task.FromException(CompletionFailure);

        Retried++;
        RetryDelays.Add(delay);
        LastRetryToken = ct;
        return Task.CompletedTask;
    }

    public Task ParkAsync(string reason, CancellationToken ct = default)
    {
        if (CompletionFailure is not null)
            return Task.FromException(CompletionFailure);

        _parked.Add(reason);
        return Task.CompletedTask;
    }
}
