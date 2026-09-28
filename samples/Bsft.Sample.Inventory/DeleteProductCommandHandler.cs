using Bsft.Core;
using Bsft.Http;
using Bsft.Mediator;
using Bsft.Sample.Inventory.Core;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Deletes a product and publishes a <see cref="ProductDeletedNotification"/>. This is the
/// reference implementation of the delete half of the CRUD convention described in
/// <c>docs/crud-conventions.md</c>.
/// </summary>
public class DeleteProductCommandHandler : ICommandHandler<DeleteProductCommand>
{
    private readonly IInventoryDbContext _db;
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteProductCommandHandler"/> class.
    /// </summary>
    /// <param name="db">The inventory database context.</param>
    /// <param name="mediator">The mediator for publishing notifications.</param>
    public DeleteProductCommandHandler(IInventoryDbContext db, IMediator mediator)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <inheritdoc />
    public async Task HandleAsync(DeleteProductCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        // Queried rather than looked up with FindAsync, for the same reason as in the update
        // handler: a primary-key lookup bypasses the global query filters, and a row belonging to
        // another tenant must be a 404 here, not a deletion.
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == command.ProductId, cancellationToken)
            .ConfigureAwait(false);

        // A missing product is reported rather than treated as an already-satisfied delete. The
        // convention chooses the truthful answer over an idempotent one: a confirm-delete dialog
        // that says "done" for a row somebody else removed hides a conflict the user should see.
        if (product is null)
            throw new NotFoundException("Product", command.ProductId);

        // Fail closed before touching anything: an If-Match that no longer names the current version
        // means another writer has changed the product since the caller last read it, and a delete of
        // a version the caller never saw is a 412 rather than a blind removal.
        if (!product.Version.ToVersionEntityTag().IsMatchedBy(command.IfMatch))
            throw new PreconditionFailedException(
                $"Product '{command.ProductId}' has been modified since it was last read.");

        // A resource-level rule, and the one the documented 422 exists for: stock on hand is a
        // fact about the warehouse, so deleting the product would delete the record of it.
        if (product.StockQuantity > 0)
            throw new BusinessRuleException(
                $"Product '{product.Name}' still holds {product.StockQuantity} units in stock and cannot be deleted.",
                "PRODUCT_HAS_STOCK");

        var name = product.Name;

        _db.Products.Remove(product);
        await SaveWithConcurrencyGuardAsync(command.ProductId, cancellationToken).ConfigureAwait(false);

        await _mediator.PublishAsync(new ProductDeletedNotification(command.ProductId, name), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Persists the removal, turning the provider's concurrency exception into the framework's own.
    /// The pre-check above answers the common stale delete deterministically; this closes the narrow
    /// window in which another writer commits between the read and the save, where the concurrency
    /// token leaves the delete matching no row and EF Core raises <see cref="DbUpdateConcurrencyException"/>.
    /// </summary>
    private async Task SaveWithConcurrencyGuardAsync(Guid productId, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PreconditionFailedException(
                $"Product '{productId}' has been modified since it was last read.");
        }
    }
}
