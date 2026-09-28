namespace Bsft.Messaging.Correlation.Tests.Fakes;

public sealed class FakeMessageCorrelationScope : IMessageCorrelationScope
{
    public string? Current { get; private set; }

    public IDisposable Use(string? correlationId)
    {
        var previous = Current;
        Current = correlationId;
        return new Restorer(this, previous);
    }

    private sealed class Restorer : IDisposable
    {
        private readonly FakeMessageCorrelationScope _owner;
        private readonly string? _previous;

        public Restorer(FakeMessageCorrelationScope owner, string? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose() => _owner.Current = _previous;
    }
}
