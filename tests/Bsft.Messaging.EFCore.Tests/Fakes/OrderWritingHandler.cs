using Bsft.Messaging.EFCore.Tests.Support;

namespace Bsft.Messaging.EFCore.Tests.Fakes;

/// <summary>
/// Writes to the very DbContext the inbox mark is staged on, which is what makes the two atomic. It
/// deliberately does not save: the commit that follows the handlers is what writes both.
/// </summary>
public sealed class OrderWritingHandler : IIntegrationEventHandler<OrderPlacedEvent>
{
    internal const string FailingOrderNumber = "FAIL";

    private readonly TestOutboxDbContext _db;
    private readonly DeliveryLog _log;

    public OrderWritingHandler(TestOutboxDbContext db, DeliveryLog log)
    {
        _db = db;
        _log = log;
    }

    public Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        _db.Orders.Add(new OrderRow { OrderNumber = integrationEvent.OrderNumber });

        if (integrationEvent.OrderNumber == FailingOrderNumber)
            throw new InvalidOperationException("the handler failed after writing");

        _log.Add(integrationEvent.OrderNumber);

        return Task.CompletedTask;
    }
}
