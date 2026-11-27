using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Handler that attempts to insert a row with a duplicate unique key (sku).</summary>
[Bsft.Mediator.Transactional]
public sealed class UniqueViolationCommandHandler(InventoryContext inventory, RetryCountingLog log)
    : ICommandHandler<UniqueViolationCommand, int>
{
    public async Task<int> HandleAsync(UniqueViolationCommand command, CancellationToken ct = default)
    {
        log.CommandAttempts++;

        // Try to insert a row with a duplicate sku (B-1 already exists)
        inventory.Stock.Add(new StockLevel { Id = 100, Sku = "B-1", Quantity = 5 });
        await inventory.SaveChangesAsync(ct);

        return log.CommandAttempts;
    }
}
