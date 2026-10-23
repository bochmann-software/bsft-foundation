using Bsft.Messaging.RabbitMQ.Tests.Support;
using RabbitMQ.Client;

namespace Bsft.Messaging.RabbitMQ.Tests.Fakes;

/// <summary>
/// A channel that records what the adapter asked the broker to do, so a test can assert the protocol
/// interaction instead of a broker's side effects.
/// </summary>
internal sealed class FakeRabbitMQChannel : IRabbitMQChannel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FakeRabbitMQChannel"/> class.
    /// </summary>
    /// <param name="signal">The wakeup to pulse on every call. Defaults to one of its own.</param>
    public FakeRabbitMQChannel(ChangeSignal? signal = null) => Signal = signal ?? new ChangeSignal();

    /// <summary>The wakeup pulsed every time this channel is called.</summary>
    public ChangeSignal Signal { get; }

    public bool IsOpen { get; set; } = true;

    public int DisposeCount { get; private set; }

    public List<(string Exchange, string Type)> DeclaredExchanges { get; } = [];

    public List<(string Queue, IDictionary<string, object?>? Arguments)> DeclaredQueues { get; } = [];

    public List<(string Queue, string Exchange, string RoutingKey)> Bindings { get; } = [];

    public List<ushort> PrefetchCounts { get; } = [];

    public List<(string Exchange, string RoutingKey, BasicProperties Properties, byte[] Body)> Published { get; } = [];

    public List<ulong> Acked { get; } = [];

    public List<(ulong DeliveryTag, bool Requeue)> Nacked { get; } = [];

    public string? ConsumedQueue { get; private set; }

    public Func<RabbitMQDelivery, Task>? DeliveryCallback { get; private set; }

    /// <summary>Set to have the next publish fail or block instead of confirming.</summary>
    public Func<CancellationToken, Task>? PublishBehaviour { get; set; }

    /// <summary>Set to have a declaration fail.</summary>
    public Exception? DeclareFailure { get; set; }

    /// <summary>Set to have the subscription fail.</summary>
    public Exception? ConsumeFailure { get; set; }

    /// <summary>Set to have an acknowledgement fail.</summary>
    public Exception? SettleFailure { get; set; }

    /// <summary>Set to have disposal fail, as a broken channel's close can.</summary>
    public Exception? DisposeFailure { get; set; }

    public Task DeclareExchangeAsync(string exchange, string type, CancellationToken ct = default)
    {
        if (DeclareFailure is not null)
        {
            Signal.Pulse();
            return Task.FromException(DeclareFailure);
        }

        DeclaredExchanges.Add((exchange, type));
        Signal.Pulse();
        return Task.CompletedTask;
    }

    public Task DeclareQueueAsync(
        string queue, IDictionary<string, object?>? arguments, CancellationToken ct = default)
    {
        if (DeclareFailure is not null)
        {
            Signal.Pulse();
            return Task.FromException(DeclareFailure);
        }

        DeclaredQueues.Add((queue, arguments));
        Signal.Pulse();
        return Task.CompletedTask;
    }

    public Task BindQueueAsync(string queue, string exchange, string routingKey, CancellationToken ct = default)
    {
        if (DeclareFailure is not null)
        {
            Signal.Pulse();
            return Task.FromException(DeclareFailure);
        }

        Bindings.Add((queue, exchange, routingKey));
        Signal.Pulse();
        return Task.CompletedTask;
    }

    public Task SetPrefetchAsync(ushort prefetchCount, CancellationToken ct = default)
    {
        PrefetchCounts.Add(prefetchCount);
        Signal.Pulse();
        return Task.CompletedTask;
    }

    public async Task PublishAsync(
        string exchange,
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken ct = default)
    {
        if (PublishBehaviour is not null)
            await PublishBehaviour(ct);

        Published.Add((exchange, routingKey, properties, body.ToArray()));
        Signal.Pulse();
    }

    public Task<string> ConsumeAsync(
        string queue, Func<RabbitMQDelivery, Task> onDeliveryAsync, CancellationToken ct = default)
    {
        if (ConsumeFailure is not null)
        {
            Signal.Pulse();
            return Task.FromException<string>(ConsumeFailure);
        }

        ConsumedQueue = queue;
        DeliveryCallback = onDeliveryAsync;
        Signal.Pulse();
        return Task.FromResult("consumer-tag");
    }

    public Task AckAsync(ulong deliveryTag, CancellationToken ct = default)
    {
        if (SettleFailure is not null)
        {
            Signal.Pulse();
            return Task.FromException(SettleFailure);
        }

        Acked.Add(deliveryTag);
        Signal.Pulse();
        return Task.CompletedTask;
    }

    public Task NackAsync(ulong deliveryTag, bool requeue, CancellationToken ct = default)
    {
        if (SettleFailure is not null)
        {
            Signal.Pulse();
            return Task.FromException(SettleFailure);
        }

        Nacked.Add((deliveryTag, requeue));
        Signal.Pulse();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        IsOpen = false;
        Signal.Pulse();

        return DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure);
    }

    /// <summary>Pushes a delivery through the subscription, as the broker would.</summary>
    public Task DeliverAsync(RabbitMQDelivery delivery) =>
        DeliveryCallback is null
            ? throw new InvalidOperationException("Nothing is subscribed to this channel.")
            : DeliveryCallback(delivery);
}
