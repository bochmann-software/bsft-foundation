using Bsft.Resilience.Mediator;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>The nested command of the scenarios, handled by another module over its own context.</summary>
[Retryable]
public sealed record ReserveStockCommand(string Name) : ICommand<int>;
