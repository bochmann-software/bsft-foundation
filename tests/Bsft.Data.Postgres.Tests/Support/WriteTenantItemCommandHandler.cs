using Bsft.Mediator;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Handler that writes to the catalog context and optionally forces a retry.</summary>
[Bsft.Mediator.Transactional]
public sealed class WriteTenantItemCommandHandler(CatalogContext catalog, TenantRetryLog _log)
    : ICommandHandler<WriteTenantItemCommand, int>
{
    public async Task<int> HandleAsync(WriteTenantItemCommand command, CancellationToken ct = default)
    {
        _log.CommandAttempts++;

        if (_log.CommandAttempts == 1)
        {
            // Force a serialization failure to trigger a retry
            await catalog.Database.ExecuteSqlRawAsync(
                "DO $$ BEGIN RAISE EXCEPTION 'test error' USING ERRCODE = '40001'; END $$", ct);
        }

        catalog.Items.Add(new CatalogItem { Name = command.Name });
        await catalog.SaveChangesAsync(ct);

        return _log.CommandAttempts;
    }
}
