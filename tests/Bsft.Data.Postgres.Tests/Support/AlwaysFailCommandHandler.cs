using Bsft.Mediator;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Handler that raises a configured SQL error.</summary>
[Bsft.Mediator.Transactional]
public sealed class AlwaysFailCommandHandler(InventoryContext inventory, RetryCountingLog log)
    : ICommandHandler<AlwaysFailCommand, int>
{
    public async Task<int> HandleAsync(AlwaysFailCommand command, CancellationToken ct = default)
    {
        log.CommandAttempts++;

        if (log.CommandAttempts <= log.FailuresBeforeSuccess)
        {
            var sql = command.Mode switch
            {
                "serialization" => "DO $$ BEGIN RAISE EXCEPTION 'test error' USING ERRCODE = '40001'; END $$",
                "deadlock" => "DO $$ BEGIN RAISE EXCEPTION 'test error' USING ERRCODE = '40P01'; END $$",
                "unique" => "DO $$ BEGIN RAISE EXCEPTION 'test error' USING ERRCODE = '23505'; END $$",
                _ => throw new ArgumentException($"Unknown mode: {command.Mode}")
            };

            await inventory.Database.ExecuteSqlRawAsync(sql, ct);
        }

        return log.CommandAttempts;
    }
}
