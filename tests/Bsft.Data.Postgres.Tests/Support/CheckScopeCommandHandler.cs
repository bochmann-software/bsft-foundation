using Bsft.Mediator;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Handler that tracks DbContext instance identity to detect fresh scopes.</summary>
[Bsft.Mediator.Transactional]
public sealed class CheckScopeCommandHandler(InventoryContext inventory, RetryCountingLog log)
    : ICommandHandler<CheckScopeCommand, string>
{
    public async Task<string> HandleAsync(CheckScopeCommand command, CancellationToken ct = default)
    {
        log.CommandAttempts++;

        if (log.CommandAttempts == 1)
        {
            // First attempt: record the context's object identity
            log.FirstAttemptContextId = inventory.GetHashCode();

            // Force a serialization failure to trigger a retry
            await inventory.Database.ExecuteSqlRawAsync(
                "DO $$ BEGIN RAISE EXCEPTION 'test error' USING ERRCODE = '40001'; END $$", ct);
        }

        // Second attempt: check if we have a fresh context instance and an empty ChangeTracker
        if (log.CommandAttempts == 2)
        {
            log.HasFreshChangeTracker = !inventory.ChangeTracker.Entries().Any();
        }

        var secondAttemptContextId = inventory.GetHashCode();
        return secondAttemptContextId != log.FirstAttemptContextId ? "fresh_scope" : "same_scope";
    }
}
