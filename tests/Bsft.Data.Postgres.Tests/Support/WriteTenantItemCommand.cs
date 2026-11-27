using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Command that writes a row to the catalog in the current tenant's database.</summary>
[Bsft.Mediator.Retryable]
public sealed record WriteTenantItemCommand(string Name) : ICommand<int>;
