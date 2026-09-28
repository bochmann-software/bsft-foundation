using Bsft.Mediator;

namespace Bsft.Sample.Inventory.Core;

/// <summary>
/// Published after a product has been updated. Carries the state the product has *after* the
/// update, so a subscriber never has to read the row back to learn what it now looks like.
/// </summary>
/// <param name="ProductId">The unique identifier of the updated product.</param>
/// <param name="Name">The name after the update.</param>
/// <param name="StockQuantity">The stock quantity after the update.</param>
/// <param name="Price">The unit price after the update.</param>
public record ProductUpdatedNotification(
    Guid ProductId,
    string Name,
    int StockQuantity,
    decimal Price) : INotification;
