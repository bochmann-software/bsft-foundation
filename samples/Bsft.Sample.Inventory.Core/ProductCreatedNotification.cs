using Bsft.Mediator;

namespace Bsft.Sample.Inventory.Core;

/// <summary>
/// Published after a product has been created. One of the three notifications that make the
/// product resource observable: every mutation of a CRUD resource answers with an event, so a
/// reader of the module can see what changed without reading the handler
/// (see <c>docs/crud-conventions.md</c> and architecture rule 1).
/// </summary>
/// <param name="ProductId">The unique identifier of the created product.</param>
/// <param name="Name">The name the product was created with.</param>
/// <param name="StockQuantity">The stock quantity the product was created with.</param>
/// <param name="Price">The unit price the product was created with.</param>
public record ProductCreatedNotification(
    Guid ProductId,
    string Name,
    int StockQuantity,
    decimal Price) : INotification;
