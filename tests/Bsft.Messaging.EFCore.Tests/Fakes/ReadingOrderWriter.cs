using Bsft.Messaging.EFCore.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Tests.Fakes;

/// <summary>
/// Queries the shared context before writing to it, which is what any handler that decides something
/// from the database does — and what makes an overlapping second handler observable.
/// </summary>
public sealed class ReadingOrderWriter : IIntegrationEventHandler<OrderPlacedEvent>
{
    private readonly TestOutboxDbContext _db;

    public ReadingOrderWriter(TestOutboxDbContext db) => _db = db;

    public async Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        // Yielding first puts both handlers past their first suspension before either touches the
        // context, so a concurrent invocation overlaps on the query rather than by luck of scheduling.
        await Task.Yield();

        _ = await _db.Orders.AsNoTracking().CountAsync(ct);

        _db.Orders.Add(new OrderRow { OrderNumber = $"{integrationEvent.OrderNumber} read" });
    }
}
