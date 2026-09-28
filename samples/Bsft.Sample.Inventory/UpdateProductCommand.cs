using Bsft.Mediator;
using Bsft.Sample.Inventory.Core;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Command to replace the editable state of an existing product. It returns the product as it
/// looks after the update, which is what <c>PUT /api/products/{id}</c> answers with, so a client
/// does not have to read the resource back to render the row it just saved.
/// </summary>
/// <param name="ProductId">The identifier of the product to update.</param>
/// <param name="Name">The new name of the product.</param>
/// <param name="StockQuantity">The new stock quantity.</param>
/// <param name="Price">The new unit price of the product.</param>
/// <param name="IfMatch">
/// The <c>If-Match</c> the caller sent, naming the version of the product it means to replace. The
/// handler refuses the write when it no longer names the current version, which is how a lost update
/// becomes a <c>412</c> instead of silently overwriting a change the caller never saw.
/// </param>
public record UpdateProductCommand(
    Guid ProductId,
    string Name,
    int StockQuantity,
    decimal Price,
    string IfMatch) : ICommand<ProductDto>;
