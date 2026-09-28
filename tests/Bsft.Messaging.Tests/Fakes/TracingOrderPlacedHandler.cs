using System.Collections.Concurrent;

namespace Bsft.Messaging.Tests.Fakes;

/// <summary>
/// Notes when it entered and when it left, with a suspension point in between, so a test can tell an
/// overlapping invocation from a sequential one from the trace alone rather than from timing.
/// </summary>
public sealed class TracingOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedEvent>
{
    private readonly ConcurrentQueue<string> _trace;
    private readonly string _name;

    public TracingOrderPlacedHandler(ConcurrentQueue<string> trace, string name)
    {
        _trace = trace;
        _name = name;
    }

    public async Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        _trace.Enqueue($"enter {_name}");
        await Task.Yield();
        _trace.Enqueue($"exit {_name}");
    }
}
