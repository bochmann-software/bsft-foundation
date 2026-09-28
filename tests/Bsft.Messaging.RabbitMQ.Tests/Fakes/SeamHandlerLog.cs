namespace Bsft.Messaging.RabbitMQ.Tests.Fakes;

/// <summary>Records what reached the far end of the pipeline, across the scopes each delivery gets.</summary>
internal sealed class SeamHandlerLog
{
    private readonly List<string> _handled = [];
    private readonly object _gate = new();

    /// <summary>Set to have every handler invocation fail, as a broken downstream dependency would.</summary>
    public bool Fails { get; set; }

    /// <summary>Gets what the handlers recorded, in the order they recorded it.</summary>
    public IReadOnlyList<string> Handled
    {
        get
        {
            lock (_gate)
                return _handled.ToArray();
        }
    }

    /// <summary>Records one handled order.</summary>
    /// <param name="orderNumber">The order the handler saw.</param>
    public void Add(string orderNumber)
    {
        lock (_gate)
            _handled.Add(orderNumber);
    }
}
