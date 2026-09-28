using Bsft.Messaging;
using Bsft.Sample.Orders.Core;
using Microsoft.Extensions.Logging;

namespace Bsft.Sample.Orders;

/// <summary>
/// The consuming end of the sample's integration event loop. It is resolved once per delivery, in the
/// scope the consumer host opens for that delivery, after the inbox has confirmed this
/// <see cref="IIntegrationEvent.EventId"/> has not been handled under this consumer name before.
/// </summary>
/// <remarks>
/// <para>
/// It only logs. What it logs is deliberately the correlation id of the request that staged the event:
/// the host restores that id around the delivery, so this line and the request that created the order
/// carry the same id even though they run on different threads, minutes apart. Nothing else here is
/// needed to make the loop work — the point is what the pipeline around it did before it was called.
/// </para>
/// <para>
/// A handler that wrote to the application's DbContext and left the saving to the inbox would have that
/// write committed by the same <c>SaveChanges</c> that records the event as processed, because the EF
/// Core inbox stages its mark on that very context. This sample does not write, so that property is
/// stated rather than shown; it is pinned by the framework's own tests.
/// </para>
/// </remarks>
public sealed class OrderPlacedIntegrationEventHandler : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    private readonly IMessageCorrelationAccessor _correlation;
    private readonly ILogger<OrderPlacedIntegrationEventHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderPlacedIntegrationEventHandler"/> class.
    /// </summary>
    /// <param name="correlation">
    /// The accessor application code reads the current correlation id through. Inside a delivery it
    /// answers with the id restored from the message, ahead of the one this background scope would
    /// otherwise mint for itself.
    /// </param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public OrderPlacedIntegrationEventHandler(
        IMessageCorrelationAccessor correlation,
        ILogger<OrderPlacedIntegrationEventHandler> logger)
    {
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        _logger.LogInformation(
            "Integration event orders.placed.v1 handled: order {OrderId}, {Quantity}x {ProductName}, staged under correlation {Correlation}",
            integrationEvent.OrderId,
            integrationEvent.Quantity,
            integrationEvent.ProductName,
            _correlation.CorrelationId);

        return Task.CompletedTask;
    }
}
