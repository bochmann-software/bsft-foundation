using Bsft.Messaging.EFCore.Tests.Support;

namespace Bsft.Messaging.EFCore.Tests.Fakes;

/// <summary>
/// Writes through the delivery's context and saves at once, which is what a handler needing the key
/// the database generates has to do. That save commits the inbox mark staged on the same context.
/// </summary>
public sealed class EagerlySavingOrderWriter : IIntegrationEventHandler<OrderPlacedEvent>
{
    private readonly TestOutboxDbContext _db;
    private readonly DeliveryLog _log;

    public EagerlySavingOrderWriter(TestOutboxDbContext db, DeliveryLog log)
    {
        _db = db;
        _log = log;
    }

    public async Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        var order = new OrderRow { OrderNumber = integrationEvent.OrderNumber };

        _db.Orders.Add(order);

        await _db.SaveChangesAsync(ct);

        _log.Add($"{integrationEvent.OrderNumber}:{order.Id}");
    }
}
