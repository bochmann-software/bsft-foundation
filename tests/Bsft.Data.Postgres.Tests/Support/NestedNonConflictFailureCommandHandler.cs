using Bsft.Mediator;
using Npgsql;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Outer command handler that sends a nested command with a non-conflict error (P0001)
/// and catches the exception to verify it is not retried.
/// </summary>
[Transactional]
public sealed class NestedNonConflictFailureCommandHandler(CatalogContext catalog, IMediator mediator, ParityTestLog log)
    : ICommandHandler<NestedNonConflictFailureCommand, int>
{
    public async Task<int> HandleAsync(NestedNonConflictFailureCommand command, CancellationToken ct = default)
    {
        log.OuterHandlerRuns++;

        catalog.Items.Add(new CatalogItem { Name = command.Name });
        await catalog.SaveChangesAsync(ct);

        try
        {
            await mediator.SendAsync(new NestedNonConflictFailureInnerCommand(), ct);
        }
        catch (PostgresException)
        {
            // The handler swallows the failure
        }

        return log.OuterHandlerRuns;
    }
}
