using Bsft.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>
/// The nested module's handler of the shared-transaction scenario. It takes the scope's provider
/// rather than its context and log, because <c>MediatorStartupValidator</c> constructs every handler
/// of this assembly against whatever container a test built, and only the scenario registers those.
/// </summary>
[Transactional]
public sealed class ReserveStockCommandHandler(IServiceProvider serviceProvider)
    : ICommandHandler<ReserveStockCommand, int>
{
    public async Task<int> HandleAsync(ReserveStockCommand command, CancellationToken ct = default)
    {
        var stock = serviceProvider.GetRequiredService<StockScenarioDbContext>();
        var log = serviceProvider.GetRequiredService<ScenarioLog>();

        log.ReservationAttempts++;
        stock.Reservations.Add(new ScenarioRow { Name = command.Name });
        await stock.SaveChangesAsync(ct);

        if (log.IsReservationRefused)
            throw new BusinessRuleException("out of stock", "OUT_OF_STOCK");

        if (log.ReservationAttempts <= log.ConflictingReservations)
            throw new ScenarioConflictException();

        return log.ReservationAttempts;
    }
}
