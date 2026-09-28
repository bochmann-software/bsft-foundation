using Bsft.Mediator;

namespace Bsft.Sample.Inventory.Core;

/// <summary>
/// Published after a product has been deleted. It carries the name alongside the identifier
/// because the row is gone by the time a subscriber runs: an identifier alone would leave the
/// subscriber with nothing it could show or log about what disappeared.
/// </summary>
/// <param name="ProductId">The unique identifier of the deleted product.</param>
/// <param name="Name">The name the product had when it was deleted.</param>
public record ProductDeletedNotification(Guid ProductId, string Name) : INotification;
