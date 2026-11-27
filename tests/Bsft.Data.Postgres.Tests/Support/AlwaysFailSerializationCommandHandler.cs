using Bsft.Mediator;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Handler that always raises a serialization failure (40001).</summary>
[Bsft.Mediator.Transactional]
public sealed class AlwaysFailSerializationCommandHandler(InventoryContext inventory, RetryCountingLog log)
    : ICommandHandler<AlwaysFailSerializationCommand, int>
{
    public async Task<int> HandleAsync(AlwaysFailSerializationCommand command, CancellationToken ct = default)
    {
        log.CommandAttempts++;

        // Always raise a serialization failure
        await inventory.Database.ExecuteSqlRawAsync(
            "DO $$ BEGIN RAISE EXCEPTION 'test error' USING ERRCODE = '40001'; END $$", ct);

        return log.CommandAttempts;
    }
}
