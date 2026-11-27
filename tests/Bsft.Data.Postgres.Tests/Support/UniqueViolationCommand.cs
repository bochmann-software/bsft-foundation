using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Command that attempts to insert a row with a duplicate unique key (sku).</summary>
[Bsft.Mediator.Retryable]
public sealed record UniqueViolationCommand : ICommand<int>;
