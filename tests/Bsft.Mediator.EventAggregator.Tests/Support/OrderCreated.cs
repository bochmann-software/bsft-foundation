using Bsft.EventAggregator.Saga;

namespace Bsft.Mediator.EventAggregator.Tests.Support;

/// <summary>A mediator notification that a saga also starts from.</summary>
public record OrderCreated(string OrderId) : INotification;
