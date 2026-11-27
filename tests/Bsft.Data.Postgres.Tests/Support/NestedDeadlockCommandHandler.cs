using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Nested command handler that raises a deadlock (40P01) on the first run only.
/// On the second run (after retry), it completes successfully.
/// </summary>
[Transactional]
public sealed class NestedDeadlockCommandHandler(InventoryContext inventory, ParityTestLog log)
    : ICommandHandler<NestedDeadlockCommand, int>
{
    public async Task<int> HandleAsync(NestedDeadlockCommand command, CancellationToken ct = default)
    {
        log.NestedHandlerRuns++;

        if (log.NestedHandlerRuns == 1)
        {
            // Raise 40P01 (deadlock) on the first run only, past EF Core's interceptor
            await RawSqlOnSharedConnection.RaiseAsync(inventory, "40P01", ct);
        }

        // Second run: complete successfully
        return log.NestedHandlerRuns;
    }
}
