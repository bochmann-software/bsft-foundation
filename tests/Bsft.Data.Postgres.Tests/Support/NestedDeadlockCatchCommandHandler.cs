using System.Runtime.CompilerServices;
using Bsft.Mediator;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Outer command handler that writes a catalog item, sends a nested command that may raise a deadlock,
/// and catches the exception when it escapes.
/// </summary>
[Transactional]
public sealed class NestedDeadlockCatchCommandHandler(CatalogContext catalog, IMediator mediator, ParityTestLog log)
    : ICommandHandler<NestedDeadlockCatchCommand, int>
{
    public async Task<int> HandleAsync(NestedDeadlockCatchCommand command, CancellationToken ct = default)
    {
        log.OuterHandlerRuns++;
        log.OuterContextInstances.Add(RuntimeHelpers.GetHashCode(catalog));

        catalog.Items.Add(new CatalogItem { Name = command.Name });
        await catalog.SaveChangesAsync(ct);

        try
        {
            await mediator.SendAsync(new NestedDeadlockCommand(), ct);
        }
        catch (PostgresException)
        {
            // The handler swallows the nested failure
        }

        return log.OuterHandlerRuns;
    }
}
