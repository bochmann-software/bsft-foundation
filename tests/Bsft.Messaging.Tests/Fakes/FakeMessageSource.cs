using System.Runtime.CompilerServices;
using Bsft.Messaging.Consuming;

namespace Bsft.Messaging.Tests.Fakes;

/// <summary>
/// A source whose stream can be made to end, to fault or to be cancelled by a token of its own, which
/// are the three things the host has to tell apart.
/// </summary>
public sealed class FakeMessageSource : IMessageSource
{
    private readonly List<IMessageDelivery> _pending = [];

    public int Subscriptions { get; private set; }

    /// <summary>Raised instead of yielding, to model a broker connection that dropped.</summary>
    public Exception? StreamFailure { get; set; }

    /// <summary>Set to end the stream by a cancellation the host did not ask for.</summary>
    public bool StreamCancelsItself { get; set; }

    /// <summary>Invoked with the subscription count each time the stream is enumerated.</summary>
    public Action<int>? OnSubscribe { get; set; }

    public void Enqueue(params IMessageDelivery[] deliveries) => _pending.AddRange(deliveries);

    public async IAsyncEnumerable<IMessageDelivery> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        Subscriptions++;
        OnSubscribe?.Invoke(Subscriptions);

        if (StreamFailure is not null)
            throw StreamFailure;

        if (StreamCancelsItself)
        {
            using var ownToken = new CancellationTokenSource();
            await ownToken.CancelAsync().ConfigureAwait(false);
            ownToken.Token.ThrowIfCancellationRequested();
        }

        var batch = _pending.ToArray();
        _pending.Clear();

        foreach (var delivery in batch)
        {
            ct.ThrowIfCancellationRequested();
            yield return delivery;
        }

        await Task.Yield();
    }
}
