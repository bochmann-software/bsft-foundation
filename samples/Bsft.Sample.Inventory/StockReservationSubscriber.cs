using Bsft.EventAggregator;
using Bsft.Sample.Inventory.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Subscribes to <see cref="StockReservationRequestedEvent"/> on the EventAggregator, checks product
/// stock, and publishes a <see cref="StockReservationResultEvent"/> with the outcome.
/// </summary>
/// <remarks>
/// A subscriber rather than a <c>*Handler</c>: it is not dispatched to, it subscribes itself at module
/// start and keeps that subscription for the life of the host, so it implements no handler contract and
/// carries no name that would suggest one. The mediator handlers in <c>Commands/</c> and <c>Queries/</c>
/// are the counter-example — each of those is resolved per dispatch and implements the matching
/// <c>Bsft.Mediator</c> interface.
/// </remarks>
public sealed class StockReservationSubscriber
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StockReservationSubscriber> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StockReservationSubscriber"/> class.
    /// </summary>
    /// <param name="eventAggregator">The event aggregator for subscribing and publishing events.</param>
    /// <param name="scopeFactory">The scope factory for creating scoped database contexts.</param>
    /// <param name="logger">The logger instance.</param>
    public StockReservationSubscriber(
        IEventAggregator eventAggregator,
        IServiceScopeFactory scopeFactory,
        ILogger<StockReservationSubscriber> logger)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Subscribes to stock reservation requests. Called during module startup.
    /// </summary>
    public void Subscribe()
    {
        _eventAggregator.Subscribe<StockReservationRequestedEvent>(HandleAsync);
    }

    /// <summary>
    /// Handles a stock reservation request: reserves the stock when available and
    /// publishes the outcome as a <see cref="StockReservationResultEvent"/>.
    /// </summary>
    /// <param name="e">The reservation request to process.</param>
    /// <returns>A task that completes once the result event has been published.</returns>
    private async Task HandleAsync(StockReservationRequestedEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IInventoryDbContext>();

        var product = await db.Products.FindAsync(e.ProductId).ConfigureAwait(false);
        if (product is not null && product.StockQuantity >= e.Quantity)
        {
            product.StockQuantity -= e.Quantity;
            await db.SaveChangesAsync().ConfigureAwait(false);

            _logger.LogInformation(
                "Stock reserved: {Quantity}x {ProductId} for order {OrderId}",
                e.Quantity, e.ProductId, e.OrderId);

            await _eventAggregator.PublishAsync(
                new StockReservationResultEvent(e.OrderId, true)).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning(
                "Stock reservation failed: not enough stock for {ProductId}, order {OrderId}",
                e.ProductId, e.OrderId);

            await _eventAggregator.PublishAsync(
                new StockReservationResultEvent(e.OrderId, false)).ConfigureAwait(false);
        }
    }
}
