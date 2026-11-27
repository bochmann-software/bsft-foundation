using Bsft.Mediator;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Reads the stock row, lets a concurrent writer update it on the raced runs, then writes it: under
/// serializable isolation the write of a raced run fails with a real serialization failure.
/// </summary>
[Transactional]
public sealed class AdjustStockCommandHandler(InventoryContext inventory, ScenarioLog log)
    : ICommandHandler<AdjustStockCommand, int>
{
    public async Task<int> HandleAsync(AdjustStockCommand command, CancellationToken ct = default)
    {
        log.AdjustmentAttempts++;

        if (log.AdjustmentAttempts <= log.SwallowedDeadlocks)
        {
            try
            {
                await inventory.Database.ExecuteSqlRawAsync(
                    "DO $$ BEGIN RAISE EXCEPTION 'deadlock' USING ERRCODE = '40P01'; END $$", ct);
            }
            catch (PostgresException)
            {
                // The handler swallows the failure; PostgreSQL has aborted the transaction anyway.
            }

            if (!log.IsWritingAfterTheSwallowedDeadlock)
                return 0;
        }

        if (log.AdjustmentAttempts <= log.FailedAdjustments)
        {
            await inventory.Database.ExecuteSqlRawAsync(
                "DO $$ BEGIN RAISE EXCEPTION 'out of stock' USING ERRCODE = 'P0001'; END $$", ct);
        }

        var stock = await inventory.Stock.SingleAsync(level => level.Id == 1, ct);

        if (log.AdjustmentAttempts <= log.RacedAdjustments)
        {
            await using var concurrent = new NpgsqlConnection(log.ConnectionString);
            await concurrent.OpenAsync(ct);
            await using var update = concurrent.CreateCommand();
            update.CommandText = "UPDATE inventory.stock SET quantity = quantity + 1 WHERE id = 1";
            await update.ExecuteNonQueryAsync(ct);
        }

        stock.Quantity += command.Delta;
        await inventory.SaveChangesAsync(ct);

        return stock.Quantity;
    }
}
