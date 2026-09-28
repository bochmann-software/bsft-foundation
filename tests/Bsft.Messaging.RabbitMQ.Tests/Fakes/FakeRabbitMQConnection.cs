using Bsft.Messaging.RabbitMQ.Connections;

namespace Bsft.Messaging.RabbitMQ.Tests.Fakes;

/// <summary>A connection that hands out prepared channels and can be told to report a recovery.</summary>
internal sealed class FakeRabbitMQConnection : IRabbitMQConnection
{
    private readonly Queue<FakeRabbitMQChannel> _channels = new();

    public bool IsOpen { get; set; } = true;

    public int DisposeCount { get; private set; }

    public List<bool> RequestedConfirmations { get; } = [];

    public List<FakeRabbitMQChannel> CreatedChannels { get; } = [];

    /// <summary>Set to have channel creation fail.</summary>
    public Exception? CreateChannelFailure { get; set; }

    public Func<Task>? RecoverySucceeded { get; set; }

    /// <summary>Queues a channel to be returned by the next creation. Without one, a fresh fake is used.</summary>
    public FakeRabbitMQConnection WithChannel(FakeRabbitMQChannel channel)
    {
        _channels.Enqueue(channel);
        return this;
    }

    public Task<IRabbitMQChannel> CreateChannelAsync(bool publisherConfirmations, CancellationToken ct = default)
    {
        if (CreateChannelFailure is not null)
            return Task.FromException<IRabbitMQChannel>(CreateChannelFailure);

        RequestedConfirmations.Add(publisherConfirmations);

        var channel = _channels.Count > 0 ? _channels.Dequeue() : new FakeRabbitMQChannel();
        CreatedChannels.Add(channel);

        return Task.FromResult<IRabbitMQChannel>(channel);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        IsOpen = false;
        return ValueTask.CompletedTask;
    }

    /// <summary>Raises the recovery notification, as the client does after it has reconnected.</summary>
    public Task RaiseRecoverySucceededAsync()
    {
        var handler = RecoverySucceeded;
        return handler is null ? Task.CompletedTask : handler();
    }

    /// <summary>Gets whether anything is currently listening for a recovery.</summary>
    public bool HasRecoverySubscriber => RecoverySucceeded is not null;
}
