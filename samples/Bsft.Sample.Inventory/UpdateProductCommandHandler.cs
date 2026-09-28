using Bsft.Core;
using Bsft.Http;
using Bsft.Mapping;
using Bsft.Mediator;
using Bsft.Sample.Inventory.Core;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Applies the update to an existing product, publishes a <see cref="ProductUpdatedNotification"/>
/// and answers with the product as it now stands. This is the reference implementation of the
/// update half of the CRUD convention described in <c>docs/crud-conventions.md</c>; the three
/// refusals below are what the documented 400/404/409 responses are produced by.
/// </summary>
public class UpdateProductCommandHandler : ICommandHandler<UpdateProductCommand, ProductDto>
{
    private readonly IInventoryDbContext _db;
    private readonly IValidator<UpdateProductCommand> _validator;
    private readonly IMapper _mapper;
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateProductCommandHandler"/> class.
    /// </summary>
    /// <param name="db">The inventory database context.</param>
    /// <param name="validator">The validator for the command's editable fields.</param>
    /// <param name="mapper">The convention-based object mapper.</param>
    /// <param name="mediator">The mediator for publishing notifications.</param>
    public UpdateProductCommandHandler(
        IInventoryDbContext db,
        IValidator<UpdateProductCommand> validator,
        IMapper mapper,
        IMediator mediator)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <inheritdoc />
    public async Task<ProductDto> HandleAsync(UpdateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        var validation = await _validator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
            throw new ValidationException(validation.Errors.ToDictionary(e => e.Key, e => e.Value));

        // Queried rather than looked up with FindAsync: a primary-key lookup can be answered from
        // the change tracker without ever reaching the database, and that path does not apply the
        // global query filters — the tenant filter among them. A row belonging to another tenant
        // must be a 404 here, not an update.
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == command.ProductId, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
            throw new NotFoundException("Product", command.ProductId);

        // Fail closed before touching anything: an If-Match that no longer names the current version
        // means the caller last saw a product another writer has since replaced, and the documented
        // answer to that is a 412, not a last-write-wins overwrite of a change it never saw.
        if (!product.Version.ToVersionEntityTag().IsMatchedBy(command.IfMatch))
            throw new PreconditionFailedException(
                $"Product '{command.ProductId}' has been modified since it was last read.");

        await RequireUniqueNameAsync(command, cancellationToken).ConfigureAwait(false);

        product.Name = command.Name;
        product.StockQuantity = command.StockQuantity;
        product.Price = command.Price;

        // A fresh token on every write is what makes the check above mean anything on the next one.
        product.Version = Guid.NewGuid();

        await SaveWithConcurrencyGuardAsync(command.ProductId, cancellationToken).ConfigureAwait(false);

        await _mediator.PublishAsync(
            new ProductUpdatedNotification(product.Id, product.Name, product.StockQuantity, product.Price),
            cancellationToken).ConfigureAwait(false);

        return _mapper.Map<Product, ProductDto>(product);
    }

    /// <summary>
    /// Persists the change, turning the provider's concurrency exception into the framework's own.
    /// The pre-check above answers the common stale write deterministically; this closes the narrow
    /// window in which another writer commits between the read and the save, where the concurrency
    /// token leaves the update matching no row and EF Core raises <see cref="DbUpdateConcurrencyException"/>.
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

    /// <summary>
    /// Refuses a rename that would collide with another product. The uniqueness of the name is the
    /// resource-level rule behind the documented 409, as opposed to the field-level rules the
    /// validator answers with a 400.
    /// </summary>
    private async Task RequireUniqueNameAsync(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        var nameTaken = await _db.Products
            .AsNoTracking()
            .AnyAsync(p => p.Id != command.ProductId && p.Name == command.Name, cancellationToken)
            .ConfigureAwait(false);

        if (nameTaken)
            throw new ConflictException($"Another product is already named '{command.Name}'.");
    }
}
