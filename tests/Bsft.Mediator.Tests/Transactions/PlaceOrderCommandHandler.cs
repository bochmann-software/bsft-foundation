using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>
/// The outer module's handler of the shared-transaction scenario: writes its row and dispatches the
/// nested module's command. It takes the scope's provider rather than its dependencies for the same
/// reason as <see cref="ReserveStockCommandHandler"/>.
/// </summary>
[Transactional]
public sealed class PlaceOrderCommandHandler(IServiceProvider serviceProvider)
    : ICommandHandler<PlaceOrderCommand, int>
{
    public async Task<int> HandleAsync(PlaceOrderCommand command, CancellationToken ct = default)
    {
        var ordering = serviceProvider.GetRequiredService<OrderingScenarioDbContext>();
        var log = serviceProvider.GetRequiredService<ScenarioLog>();

        log.OrderAttempts++;
        ordering.Orders.Add(new ScenarioRow { Name = command.Name });
        await ordering.SaveChangesAsync(ct);

        await serviceProvider.GetRequiredService<IMediator>().SendAsync(new ReserveStockCommand(command.Name), ct);

        return log.OrderAttempts;
    }
}
