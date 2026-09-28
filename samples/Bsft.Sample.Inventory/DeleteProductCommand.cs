using Bsft.Mediator;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Command to delete a product. It produces no result: <c>DELETE /api/products/{id}</c> answers
/// with 204 and an empty body, so there is nothing for the command to return.
/// </summary>
/// <param name="ProductId">The identifier of the product to delete.</param>
/// <param name="IfMatch">
/// The <c>If-Match</c> the caller sent, naming the version of the product it means to delete. The
/// handler refuses the delete when it no longer names the current version, so a product another
/// writer has changed since the caller last read it is a <c>412</c> rather than a blind removal.
/// </param>
public record DeleteProductCommand(Guid ProductId, string IfMatch) : ICommand;
