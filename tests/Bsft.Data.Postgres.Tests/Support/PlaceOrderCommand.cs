using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>The outermost command: writes a catalog row, then adjusts stock in the inventory module.</summary>
[Retryable]
public sealed record PlaceOrderCommand(string Name) : ICommand<int>;
