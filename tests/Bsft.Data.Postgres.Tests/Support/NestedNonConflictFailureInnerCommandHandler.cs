using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Nested command handler that raises a non-conflict error (P0001) to verify
/// that the outer handler catches it and the transaction is marked for rollback.
/// </summary>
[Transactional]
public sealed class NestedNonConflictFailureInnerCommandHandler(InventoryContext inventory)
    : ICommandHandler<NestedNonConflictFailureInnerCommand, int>
{
    public async Task<int> HandleAsync(NestedNonConflictFailureInnerCommand command, CancellationToken ct = default)
    {
        // Raise P0001 (non-conflict error) past EF Core's interceptor; the outer handler catches it
        await RawSqlOnSharedConnection.RaiseAsync(inventory, "P0001", ct);

        return 0;
    }
}
