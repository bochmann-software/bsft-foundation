using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>The nested command, handled by the inventory module over its own context.</summary>
[Retryable]
public sealed record AdjustStockCommand(int Delta) : ICommand<int>;
