using Bsft.Messaging.RabbitMQ.Connections;
using Bsft.Messaging.RabbitMQ.Tests.Support;

namespace Bsft.Messaging.RabbitMQ.Tests.Fakes;

/// <summary>A connection that hands out prepared channels and can be told to report a recovery.</summary>
internal sealed class FakeRabbitMQConnection : IRabbitMQConnection
{
    private readonly Queue<FakeRabbitMQChannel> _channels = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeRabbitMQConnection"/> class.
    /// </summary>
    /// <param name="signal">
    /// The wakeup to pulse on every call, and to hand to a channel this connection creates itself
    /// (rather than one a test queued through <see cref="WithChannel"/>) so a test waiting on this
    /// connection also wakes up for what that channel does. Defaults to one of its own.
    /// </param>
    public FakeRabbitMQConnection(ChangeSignal? signal = null) => Signal = signal ?? new ChangeSignal();

    /// <summary>The wakeup pulsed every time this connection is called.</summary>
    public ChangeSignal Signal { get; }

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
        {
            Signal.Pulse();
            return Task.FromException<IRabbitMQChannel>(CreateChannelFailure);
        }

        RequestedConfirmations.Add(publisherConfirmations);

        var channel = _channels.Count > 0 ? _channels.Dequeue() : new FakeRabbitMQChannel(Signal);
        CreatedChannels.Add(channel);
        Signal.Pulse();

        return Task.FromResult<IRabbitMQChannel>(channel);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        IsOpen = false;
        Signal.Pulse();
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
