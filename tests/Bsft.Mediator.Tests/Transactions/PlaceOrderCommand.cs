using Bsft.Resilience.Mediator;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>The outermost command of the scenarios: writes an order, then reserves stock in another module.</summary>
[Retryable]
public sealed record PlaceOrderCommand(string Name) : ICommand<int>;
