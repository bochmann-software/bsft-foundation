using Bsft.EventAggregator.Saga;

namespace Bsft.Mediator.EventAggregator.Tests.Support;

/// <summary>Saga state carrying the order the flow was started for.</summary>
public record OrderSagaState
{
    public string? OrderId { get; set; }
}
