using Bsft.Core;
using Bsft.Mediator;
using Bsft.Messaging.Publishing;
using Bsft.Sample.Orders.Core;

namespace Bsft.Sample.Orders;

/// <summary>
/// Resolves the product from the database, creates the order with a snapshot of the
/// current product name and price, and publishes an <see cref="OrderCreatedNotification"/>
/// through the mediator pipeline (which forwards it to the EventAggregator for the saga).
/// </summary>
/// <remarks>
/// It also stages an <see cref="OrderPlacedIntegrationEvent"/> — the outward-facing half of the same
/// fact. Staging is not publishing: the event is handed to the scoped collector and the
/// <c>OutboxInterceptor</c> turns it into an outbox row inside the very <c>SaveChanges</c> below, so
/// the order and the event it announces are one commit. Nothing has left the process when this method
/// returns; the dispatcher picks the row up afterwards.
/// </remarks>
public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, Guid>
{
    private readonly IOrderDbContext _db;
    private readonly IMediator _mediator;
    private readonly IIntegrationEventCollector _integrationEvents;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateOrderCommandHandler"/> class.
    /// </summary>
    /// <param name="db">The order database context.</param>
    /// <param name="mediator">The mediator for publishing notifications.</param>
    /// <param name="integrationEvents">The scoped collector integration events are staged on.</param>
    public CreateOrderCommandHandler(
        IOrderDbContext db, IMediator mediator, IIntegrationEventCollector integrationEvents)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _integrationEvents = integrationEvents ?? throw new ArgumentNullException(nameof(integrationEvents));
    }

    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.FindAsync(new object[] { command.ProductId }, cancellationToken).ConfigureAwait(false);
        if (product is null)
            throw new NotFoundException("Product", command.ProductId);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            ProductId = command.ProductId,
            ProductName = product.Name,
            Quantity = command.Quantity,
            UnitPrice = product.Price,
            Status = OrderStatus.Pending
        };

        _db.Orders.Add(order);

        // Staged before the save, because the interceptor drains the collector from inside it. Staging
        // after the save would leave the event for whichever SaveChanges happens next — or for none.
        _integrationEvents.Enqueue(new OrderPlacedIntegrationEvent
        {
            OccurredAt = order.CreatedAt,
            OrderId = order.Id,
            ProductName = order.ProductName,
            Quantity = order.Quantity,
        });

        // One transaction: the order row, and the outbox row announcing it.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _mediator.PublishAsync(
            new OrderCreatedNotification(order.Id, order.ProductId, order.ProductName, order.Quantity),
            cancellationToken).ConfigureAwait(false);

        return order.Id;
    }
}
