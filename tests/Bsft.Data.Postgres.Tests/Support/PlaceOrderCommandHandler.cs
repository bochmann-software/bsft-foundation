using Bsft.Mediator;
using Npgsql;

namespace Bsft.Data.Postgres.Tests.Support;

[Transactional]
public sealed class PlaceOrderCommandHandler(CatalogContext catalog, IMediator mediator, ScenarioLog log)
    : ICommandHandler<PlaceOrderCommand, int>
{
    public async Task<int> HandleAsync(PlaceOrderCommand command, CancellationToken ct = default)
    {
        log.OrderAttempts++;
        catalog.Items.Add(new CatalogItem { Name = command.Name });
        await catalog.SaveChangesAsync(ct);

        try
        {
            await mediator.SendAsync(new AdjustStockCommand(1), ct);
        }
        catch (PostgresException) when (log.IsSwallowingTheAdjustmentFailure)
        {
            // The handler swallows the nested failure and completes as if the stock were adjusted.
        }

        return log.OrderAttempts;
    }
}
