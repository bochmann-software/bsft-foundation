using Bsft.EventAggregator.Saga;

namespace Bsft.Mediator.EventAggregator.Tests.Support;

/// <summary>Records what ran, so a test can assert both the fact and the order.</summary>
public sealed class DeliveryTrace
{
    private readonly List<string> _entries = [];
    private readonly object _lock = new();

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public void Add(string entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }
    }
}
