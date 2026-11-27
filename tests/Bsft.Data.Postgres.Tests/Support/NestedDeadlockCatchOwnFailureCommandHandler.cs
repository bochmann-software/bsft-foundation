using Bsft.Mediator;
using Npgsql;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Outer command handler that writes a catalog item, sends a nested command that raises 40P01,
/// catches the PostgresException, and throws its own exception.
/// </summary>
[Transactional]
public sealed class NestedDeadlockCatchOwnFailureCommandHandler(CatalogContext catalog, IMediator mediator, ParityTestLog log)
    : ICommandHandler<NestedDeadlockCatchOwnFailureCommand, int>
{
    public async Task<int> HandleAsync(NestedDeadlockCatchOwnFailureCommand command, CancellationToken ct = default)
    {
        log.OuterHandlerRuns++;

        catalog.Items.Add(new CatalogItem { Name = command.Name });
        await catalog.SaveChangesAsync(ct);

        try
        {
            await mediator.SendAsync(new NestedDeadlockCommand(), ct);
        }
        catch (PostgresException)
        {
            // The handler catches the nested conflict and throws its own exception
            throw new InvalidOperationException("own failure");
        }

        return log.OuterHandlerRuns;
    }
}
