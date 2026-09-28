using Bsft.Messaging.EFCore.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Tests.Fakes;

/// <summary>The second registration of the same shape: a distinct type, because handlers are
/// registered by implementation type and the same one twice is one handler.</summary>
public sealed class SecondReadingOrderWriter : IIntegrationEventHandler<OrderPlacedEvent>
{
    private readonly TestOutboxDbContext _db;

    public SecondReadingOrderWriter(TestOutboxDbContext db) => _db = db;

    public async Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        await Task.Yield();

        _ = await _db.Orders.AsNoTracking().CountAsync(ct);

        _db.Orders.Add(new OrderRow { OrderNumber = $"{integrationEvent.OrderNumber} second" });
    }
}
