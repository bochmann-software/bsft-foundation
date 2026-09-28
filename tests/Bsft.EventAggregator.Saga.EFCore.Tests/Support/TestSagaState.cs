namespace Bsft.EventAggregator.Saga.EFCore.Tests.Support;

/// <summary>The saga state the store tests round-trip through the database.</summary>
public sealed class TestSagaState
{
    public Guid OrderId { get; set; }

    public bool Paid { get; set; }
}
