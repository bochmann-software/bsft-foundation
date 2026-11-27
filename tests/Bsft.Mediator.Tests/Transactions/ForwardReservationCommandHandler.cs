using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>
/// Forwards to the nested module without a transaction of its own. It takes the scope's provider for
/// the same reason as <see cref="ReserveStockCommandHandler"/>.
/// </summary>
public sealed class ForwardReservationCommandHandler(IServiceProvider serviceProvider)
    : ICommandHandler<ForwardReservationCommand, int>
{
    public async Task<int> HandleAsync(ForwardReservationCommand command, CancellationToken ct = default)
    {
        serviceProvider.GetRequiredService<ScenarioLog>().ForwardAttempts++;

        return await serviceProvider.GetRequiredService<IMediator>().SendAsync(new ReserveStockCommand(command.Name), ct);
    }
}
